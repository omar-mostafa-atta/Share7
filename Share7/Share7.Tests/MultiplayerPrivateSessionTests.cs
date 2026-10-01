using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Private sessions: entered with the code the host reads out, and — once the rollout switch is on —
/// only with it.
/// <para>
/// **Before this, "private" meant only "not offered by matchmaking".** Every private session minted a
/// code and nothing accepted one, so the only way in was by session id, which anyone who learned it
/// could use.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerPrivateSessionTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerPrivateSessionTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>A private session confirmed up to <c>Created</c>, and its code.</summary>
    private async Task<(Guid HostId, Guid SessionId, string Code)> OpenPrivateAsync(MultiplayerOptions? options = null)
    {
        await using var context = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        await MultiplayerTest.SetSeatsAsync(context, curriculum.GameId, 1, 4);
        var hostId = await TestData.CreateUserAsync(context);

        var sessions = MultiplayerTest.Sessions(context, options);
        var created = await sessions.CreateAsync(
            hostId, MultiplayerTest.CreateRequest(curriculum.GameId, visibility: SessionVisibility.Private));

        await sessions.StartAsync(hostId, created.Value!.Id, new StartMultiplayerSessionRequest());

        return (hostId, created.Value.Id, created.Value.JoinCode!);
    }

    private static JoinMultiplayerSessionByCodeRequest ByCode(string code, string? requestId = null) =>
        new() { JoinCode = code, ProtocolVersion = 1, RequestId = requestId };

    [Fact]
    public async Task A_friend_joins_with_the_code_the_host_reads_out()
    {
        var room = await OpenPrivateAsync();

        await using var context = _fixture.CreateContext();
        var friendId = await TestData.CreateUserAsync(context);

        var joined = await MultiplayerTest.Sessions(context).JoinByCodeAsync(friendId, ByCode(room.Code));

        Assert.True(joined.Succeeded, joined.Error?.Code);
        Assert.Equal(room.SessionId, joined.Value!.Id);
        Assert.Contains(joined.Value.Players, p => p.UserId == friendId);
    }

    [Fact]
    public async Task A_code_is_read_the_way_people_type_it()
    {
        var room = await OpenPrivateAsync();

        await using var context = _fixture.CreateContext();
        var friendId = await TestData.CreateUserAsync(context);

        // Lower case, and split the way a code is read aloud: "k three f — nine q a".
        var typed = $"{room.Code[..3].ToLowerInvariant()} - {room.Code[3..].ToLowerInvariant()}";
        var joined = await MultiplayerTest.Sessions(context).JoinByCodeAsync(friendId, ByCode(typed));

        Assert.True(joined.Succeeded, joined.Error?.Code);
        Assert.Equal(room.SessionId, joined.Value!.Id);
    }

    [Theory]
    [InlineData("ZZZZZZ")]
    [InlineData("")]
    [InlineData("far-too-long-to-be-a-code")]
    public async Task Every_code_that_opens_no_room_gets_the_same_answer(string code)
    {
        await using var context = _fixture.CreateContext();
        var guesserId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Sessions(context).JoinByCodeAsync(guesserId, ByCode(code));

        // Unknown, malformed and empty are indistinguishable, so a guesser learns nothing from the
        // shape of the refusal.
        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
        Assert.Equal("SESSION_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task An_ended_session_s_code_opens_nothing()
    {
        var room = await OpenPrivateAsync();

        await using var closing = _fixture.CreateContext();
        await MultiplayerTest.Sessions(closing).CloseAsync(room.HostId, room.SessionId, new CloseMultiplayerSessionRequest());

        await using var context = _fixture.CreateContext();
        var lateId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Sessions(context).JoinByCodeAsync(lateId, ByCode(room.Code));

        Assert.Equal("SESSION_NOT_FOUND", result.Error?.Code);
    }

    [Fact]
    public async Task A_retried_join_by_code_is_answered_from_the_first()
    {
        var room = await OpenPrivateAsync();

        await using var context = _fixture.CreateContext();
        var friendId = await TestData.CreateUserAsync(context);
        var sessions = MultiplayerTest.Sessions(context);

        var first = await sessions.JoinByCodeAsync(friendId, ByCode(room.Code, "code-retry"));
        var retry = await sessions.JoinByCodeAsync(friendId, ByCode(room.Code, "code-retry"));

        Assert.True(retry.Succeeded, retry.Error?.Code);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
    }

    [Fact]
    public async Task With_the_switch_off_a_private_session_still_opens_by_id()
    {
        // Today's behaviour, kept on purpose: the shipped build may join a friend's room this way.
        var room = await OpenPrivateAsync();

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        var joined = await MultiplayerTest.Sessions(context)
            .JoinAsync(strangerId, room.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.True(joined.Succeeded, joined.Error?.Code);
    }

    [Fact]
    public async Task With_the_switch_on_only_the_code_opens_a_private_session()
    {
        var locked = MultiplayerTest.Options(o => o.RequireJoinCodeForPrivateSessions = true);
        var room = await OpenPrivateAsync(locked);

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);
        var friendId = await TestData.CreateUserAsync(context);
        var sessions = MultiplayerTest.Sessions(context, locked);

        // By id, a stranger is told the session does not exist — the same answer as for an id nobody
        // holds, so the id route cannot be used to find private rooms either.
        var byId = await sessions.JoinAsync(
            strangerId, room.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });
        Assert.Equal("SESSION_NOT_FOUND", byId.Error?.Code);

        var byCode = await sessions.JoinByCodeAsync(friendId, ByCode(room.Code));
        Assert.True(byCode.Succeeded, byCode.Error?.Code);
    }

    [Fact]
    public async Task With_the_switch_on_a_player_who_dropped_can_still_rejoin_by_id()
    {
        var locked = MultiplayerTest.Options(o => o.RequireJoinCodeForPrivateSessions = true);
        var room = await OpenPrivateAsync(locked);

        await using var context = _fixture.CreateContext();
        var friendId = await TestData.CreateUserAsync(context);
        var sessions = MultiplayerTest.Sessions(context, locked);

        await sessions.JoinByCodeAsync(friendId, ByCode(room.Code));
        await sessions.LeaveAsync(friendId, room.SessionId, new LeaveMultiplayerSessionRequest());

        // Someone who has held a seat here is reconnecting, not intruding. Locking them out of their
        // own match because their app kept the id rather than the code would be the wrong trade.
        var back = await sessions.JoinAsync(
            friendId, room.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.True(back.Succeeded, back.Error?.Code);
    }

    [Fact]
    public async Task Join_codes_use_only_characters_a_child_can_read_unambiguously()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        for (var i = 0; i < 5; i++)
        {
            var room = await OpenPrivateAsync();

            Assert.Equal(6, room.Code.Length);
            Assert.All(room.Code, c => Assert.Contains(c, alphabet));

            await using var context = _fixture.CreateContext();
            await MultiplayerTest.Sessions(context).CloseAsync(room.HostId, room.SessionId, new CloseMultiplayerSessionRequest());
        }
    }
}
