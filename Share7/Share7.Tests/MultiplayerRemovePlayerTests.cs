using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// A host removing a player from the lobby — and the removal actually keeping them out.
/// <para>
/// **The safety case this exists for:** a join code reaches someone it should not have, and the host
/// needs that person gone before the match starts. Marking the seat removed is not enough on its own;
/// every door back in — by id, by code, through matchmaking, even just watching the roster — has to
/// be shut, including one being opened at the same moment.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerRemovePlayerTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerRemovePlayerTests(SqlServerFixture fixture) => _fixture = fixture;

    private static RemovePlayerRequest Remove(Guid userId, string? requestId = null) =>
        new() { UserId = userId, RequestId = requestId };

    private static JoinMultiplayerSessionRequest Join() => new() { ProtocolVersion = 1 };

    [Fact]
    public async Task The_host_removes_a_player_from_the_lobby()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var unwantedId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var removed = await MultiplayerTest.Sessions(context)
            .RemovePlayerAsync(session.HostId, session.SessionId, Remove(unwantedId));

        Assert.True(removed.Succeeded, removed.Error?.Code);
        Assert.DoesNotContain(removed.Value!.Players, p => p.UserId == unwantedId);
        Assert.Equal(1, removed.Value.CurrentPlayerCount);

        await using var check = _fixture.CreateContext();
        var seat = (await MultiplayerTest.ReadPlayersAsync(check, session.SessionId)).Single(p => p.UserId == unwantedId);
        Assert.Equal(SessionPlayerStatus.Removed, seat.Status);
        Assert.True(await check.MultiplayerSessionBans.AnyAsync(b => b.SessionId == session.SessionId && b.UserId == unwantedId));
    }

    [Fact]
    public async Task A_removed_player_cannot_take_a_seat_again_by_id()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var unwantedId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);
        await sessions.RemovePlayerAsync(session.HostId, session.SessionId, Remove(unwantedId));

        var back = await sessions.JoinAsync(unwantedId, session.SessionId, Join());

        // Told plainly — only the removed account ever receives this.
        Assert.False(back.Succeeded);
        Assert.Equal(ServiceErrorKind.Forbidden, back.ErrorKind);
        Assert.Equal("SESSION_REMOVED", back.Error!.Code);
    }

    [Fact]
    public async Task A_removed_player_cannot_come_back_with_the_code()
    {
        await using var setup = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(setup);
        await MultiplayerTest.SetSeatsAsync(setup, curriculum.GameId, 1, 4);
        var hostId = await TestData.CreateUserAsync(setup);
        var unwantedId = await TestData.CreateUserAsync(setup);

        var sessions = MultiplayerTest.Sessions(setup);
        var created = await sessions.CreateAsync(
            hostId, MultiplayerTest.CreateRequest(curriculum.GameId, visibility: SessionVisibility.Private));
        await sessions.StartAsync(hostId, created.Value!.Id, new StartMultiplayerSessionRequest());

        var code = new JoinMultiplayerSessionByCodeRequest { JoinCode = created.Value.JoinCode!, ProtocolVersion = 1 };

        await using var context = _fixture.CreateContext();
        var room = MultiplayerTest.Sessions(context);
        Assert.True((await room.JoinByCodeAsync(unwantedId, code)).Succeeded);

        await room.RemovePlayerAsync(hostId, created.Value.Id, Remove(unwantedId));

        var back = await room.JoinByCodeAsync(unwantedId, code);
        Assert.Equal("SESSION_REMOVED", back.Error?.Code);
    }

    [Fact]
    public async Task Matchmaking_never_hands_a_removed_player_the_lobby_they_were_removed_from()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var unwantedId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        await MultiplayerTest.Sessions(context).RemovePlayerAsync(session.HostId, session.SessionId, Remove(unwantedId));

        // The lobby is public, open, fresh and has room: exactly what matchmaking would offer first.
        var searched = await MultiplayerTest.Matchmaking(context).MatchmakeAsync(unwantedId, new MatchmakeRequest
        {
            GameId = session.GameId,
            ProtocolVersion = 1,
            CreateIfNoneFound = true,
            TransportSessionName = MultiplayerTest.NewTransportName()
        });

        Assert.True(searched.Succeeded, searched.Error?.Code);
        Assert.NotEqual(session.SessionId, searched.Value!.Session!.Id);
    }

    [Fact]
    public async Task A_removed_player_loses_sight_of_the_room()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var unwantedId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);
        await sessions.RemovePlayerAsync(session.HostId, session.SessionId, Remove(unwantedId));

        // Being removed and then being able to keep watching who is in the lobby is exactly what
        // removal is meant to stop.
        Assert.Equal("SESSION_NOT_FOUND", (await sessions.GetAsync(unwantedId, session.SessionId)).Error?.Code);
        Assert.Equal("SESSION_NOT_FOUND", (await sessions.GetPlayersAsync(unwantedId, session.SessionId)).Error?.Code);
        Assert.Equal("SESSION_NOT_FOUND",
            (await sessions.LeaveAsync(unwantedId, session.SessionId, new LeaveMultiplayerSessionRequest())).Error?.Code);
        Assert.Empty((await sessions.ListForUserAsync(unwantedId, new MultiplayerSessionQuery())).Value!);
    }

    [Fact]
    public async Task A_removal_racing_the_players_rejoin_still_keeps_them_out()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var unwantedId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var leaving = _fixture.CreateContext();
        await MultiplayerTest.Sessions(leaving).LeaveAsync(unwantedId, session.SessionId, new LeaveMultiplayerSessionRequest());

        // They left, the host moves to remove them, and they rejoin in the gap before the removal acts.
        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            var rejoined = await MultiplayerTest.Sessions(other).JoinAsync(unwantedId, session.SessionId, Join());
            Assert.True(rejoined.Succeeded, rejoined.Error?.Code);
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var removed = await MultiplayerTest.Sessions(context)
            .RemovePlayerAsync(session.HostId, session.SessionId, Remove(unwantedId));

        Assert.True(race.Fired);
        Assert.True(removed.Succeeded, removed.Error?.Code);

        // The seat taken in the gap is the one the removal takes away.
        await using var check = _fixture.CreateContext();
        var seats = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);
        Assert.DoesNotContain(seats, p => p.UserId == unwantedId && !p.Status.HasDeparted());
        Assert.Equal(1, (await MultiplayerTest.ReadSessionAsync(check, session.SessionId)).CurrentPlayerCount);

        var again = await MultiplayerTest.Sessions(check).JoinAsync(unwantedId, session.SessionId, Join());
        Assert.Equal("SESSION_REMOVED", again.Error?.Code);
    }

    [Fact]
    public async Task Removing_twice_is_one_removal()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var unwantedId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        var first = await sessions.RemovePlayerAsync(session.HostId, session.SessionId, Remove(unwantedId));
        var second = await sessions.RemovePlayerAsync(session.HostId, session.SessionId, Remove(unwantedId));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded, second.Error?.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessionBans.CountAsync(b => b.SessionId == session.SessionId));
        Assert.Equal(1, (await MultiplayerTest.ReadSessionAsync(check, session.SessionId)).CurrentPlayerCount);
    }

    [Fact]
    public async Task Only_the_host_removes()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var memberId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);
        var otherId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var result = await MultiplayerTest.Sessions(context).RemovePlayerAsync(memberId, session.SessionId, Remove(otherId));

        Assert.Equal("NOT_SESSION_HOST", result.Error?.Code);
    }

    [Fact]
    public async Task The_host_cannot_remove_themselves_or_a_stranger()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);
        var sessions = MultiplayerTest.Sessions(context);

        Assert.Equal("VALIDATION_FAILED",
            (await sessions.RemovePlayerAsync(session.HostId, session.SessionId, Remove(session.HostId))).Error?.Code);
        Assert.Equal("NOT_SESSION_MEMBER",
            (await sessions.RemovePlayerAsync(session.HostId, session.SessionId, Remove(strangerId))).Error?.Code);
    }

    [Fact]
    public async Task A_running_match_cannot_lose_a_player_to_the_host()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var playerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);
        await sessions.StartAsync(session.HostId, session.SessionId, new StartMultiplayerSessionRequest());

        // Removing a player mid-match would let a host hand a child a loss. Closing the match is
        // still available to a host who wants it over.
        var result = await sessions.RemovePlayerAsync(session.HostId, session.SessionId, Remove(playerId));

        Assert.Equal("SESSION_INVALID_TRANSITION", result.Error?.Code);
    }
}
