using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// A host who suspects their room's code reached the wrong person gets a new one, and the old one
/// stops working — without disturbing anyone already seated.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerJoinCodeRotationTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerJoinCodeRotationTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>A session confirmed up to <c>Created</c>, and its code (null when public).</summary>
    private async Task<(Guid HostId, Guid SessionId, string? Code)> OpenAsync(
        SessionVisibility visibility = SessionVisibility.Private)
    {
        await using var context = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        await MultiplayerTest.SetSeatsAsync(context, curriculum.GameId, 1, 4);
        var hostId = await TestData.CreateUserAsync(context);

        var sessions = MultiplayerTest.Sessions(context);
        var created = await sessions.CreateAsync(
            hostId, MultiplayerTest.CreateRequest(curriculum.GameId, visibility: visibility));

        await sessions.StartAsync(hostId, created.Value!.Id, new StartMultiplayerSessionRequest());

        return (hostId, created.Value.Id, created.Value.JoinCode);
    }

    private static JoinMultiplayerSessionByCodeRequest ByCode(string code) =>
        new() { JoinCode = code, ProtocolVersion = 1 };

    private static RotateJoinCodeRequest Rotate(string? requestId = null) => new() { RequestId = requestId };

    private async Task<Guid> JoinByCodeAsync(string code)
    {
        await using var context = _fixture.CreateContext();
        var friendId = await TestData.CreateUserAsync(context);

        var joined = await MultiplayerTest.Sessions(context).JoinByCodeAsync(friendId, ByCode(code));
        Assert.True(joined.Succeeded, joined.Error?.Code);

        return friendId;
    }

    [Fact]
    public async Task The_new_code_opens_the_room_and_the_old_one_opens_nothing()
    {
        var room = await OpenAsync();

        await using var context = _fixture.CreateContext();
        var rotated = await MultiplayerTest.Sessions(context).RotateJoinCodeAsync(room.HostId, room.SessionId, Rotate());

        Assert.True(rotated.Succeeded, rotated.Error?.Code);
        var fresh = rotated.Value!.JoinCode!;
        Assert.NotEqual(room.Code, fresh);

        await using var guessing = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(guessing);
        var withOld = await MultiplayerTest.Sessions(guessing).JoinByCodeAsync(strangerId, ByCode(room.Code!));

        // Refused exactly as a code that never existed — a leaked code tells its holder nothing.
        Assert.Equal("SESSION_NOT_FOUND", withOld.Error?.Code);

        var friendId = await JoinByCodeAsync(fresh);

        await using var check = _fixture.CreateContext();
        Assert.Contains(await MultiplayerTest.ReadPlayersAsync(check, room.SessionId), p => p.UserId == friendId);
    }

    [Fact]
    public async Task Players_already_seated_keep_their_seats()
    {
        var room = await OpenAsync();
        var friendId = await JoinByCodeAsync(room.Code!);

        await using var context = _fixture.CreateContext();
        var rotated = await MultiplayerTest.Sessions(context).RotateJoinCodeAsync(room.HostId, room.SessionId, Rotate());

        Assert.True(rotated.Succeeded, rotated.Error?.Code);
        Assert.Contains(rotated.Value!.Players, p => p.UserId == friendId);
        Assert.Equal(2, rotated.Value.CurrentPlayerCount);
    }

    [Fact]
    public async Task A_retried_rotation_hands_back_the_code_it_already_minted()
    {
        var room = await OpenAsync();

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        var first = await sessions.RotateJoinCodeAsync(room.HostId, room.SessionId, Rotate("rotate-retry"));
        var retry = await sessions.RotateJoinCodeAsync(room.HostId, room.SessionId, Rotate("rotate-retry"));

        // The host may already have read the first new code out; a retry minting a third would strand
        // the friend holding the second.
        Assert.Equal(first.Value!.JoinCode, retry.Value!.JoinCode);

        await using var check = _fixture.CreateContext();
        Assert.Equal(first.Value.JoinCode, (await MultiplayerTest.ReadSessionAsync(check, room.SessionId)).JoinCode);
    }

    [Fact]
    public async Task Only_the_host_can_change_the_code()
    {
        var room = await OpenAsync();
        var friendId = await JoinByCodeAsync(room.Code!);

        await using var context = _fixture.CreateContext();
        var refused = await MultiplayerTest.Sessions(context).RotateJoinCodeAsync(friendId, room.SessionId, Rotate());

        Assert.Equal("NOT_SESSION_HOST", refused.Error?.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(room.Code, (await MultiplayerTest.ReadSessionAsync(check, room.SessionId)).JoinCode);
    }

    [Fact]
    public async Task A_stranger_cannot_learn_the_session_exists()
    {
        var room = await OpenAsync();

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        var refused = await MultiplayerTest.Sessions(context).RotateJoinCodeAsync(strangerId, room.SessionId, Rotate());

        Assert.Equal("SESSION_NOT_FOUND", refused.Error?.Code);
    }

    [Fact]
    public async Task A_public_session_has_no_code_to_change()
    {
        var room = await OpenAsync(SessionVisibility.Public);

        await using var context = _fixture.CreateContext();
        var refused = await MultiplayerTest.Sessions(context).RotateJoinCodeAsync(room.HostId, room.SessionId, Rotate());

        Assert.Equal("VALIDATION_FAILED", refused.Error?.Code);

        await using var check = _fixture.CreateContext();
        Assert.Null((await MultiplayerTest.ReadSessionAsync(check, room.SessionId)).JoinCode);
    }

    [Fact]
    public async Task A_match_in_progress_keeps_its_code()
    {
        var room = await OpenAsync();

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);
        await sessions.StartAsync(room.HostId, room.SessionId, new StartMultiplayerSessionRequest());

        var refused = await sessions.RotateJoinCodeAsync(room.HostId, room.SessionId, Rotate());

        Assert.Equal("SESSION_INVALID_TRANSITION", refused.Error?.Code);
    }

    [Fact]
    public async Task A_host_who_loses_the_room_mid_rotation_changes_nothing()
    {
        var room = await OpenAsync();
        var friendId = await JoinByCodeAsync(room.Code!);

        // Authority passes to the friend in the instant between the rotation's checks and its write.
        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            await other.MultiplayerSessions
                .Where(s => s.Id == room.SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.HostUserId, friendId));
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var refused = await MultiplayerTest.Sessions(context).RotateJoinCodeAsync(room.HostId, room.SessionId, Rotate());

        Assert.True(race.Fired);
        Assert.Equal("NOT_SESSION_HOST", refused.Error?.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(room.Code, (await MultiplayerTest.ReadSessionAsync(check, room.SessionId)).JoinCode);
    }
}
