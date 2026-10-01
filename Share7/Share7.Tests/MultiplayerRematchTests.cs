using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// "Rematch" on the results screen: the same players, the same game and mode, in a new private room
/// that only they can sit in — and one room however many of them press it at once.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerRematchTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerRematchTests(SqlServerFixture fixture) => _fixture = fixture;

    private sealed record PlayedMatch(Guid HostId, Guid GuestId, Guid SessionId, Guid GameId);

    /// <summary>Two players, a match started and closed by its host.</summary>
    private async Task<PlayedMatch> PlayedMatchAsync()
    {
        var open = await MultiplayerTest.OpenAsync(_fixture);
        var guestId = await MultiplayerTest.JoinAsync(_fixture, open.SessionId);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        var started = await sessions.StartAsync(open.HostId, open.SessionId, new StartMultiplayerSessionRequest());
        Assert.Equal(MultiplayerSessionState.Running, started.Value!.State);

        await sessions.CloseAsync(open.HostId, open.SessionId, new CloseMultiplayerSessionRequest());

        return new PlayedMatch(open.HostId, guestId, open.SessionId, open.GameId);
    }

    private static RematchRequest Ask(string? requestId = null) => new()
    {
        TransportSessionName = MultiplayerTest.NewTransportName(),
        ProtocolVersion = 1,
        RequestId = requestId
    };

    private static JoinMultiplayerSessionRequest Join() => new() { ProtocolVersion = 1 };

    /// <summary>Brings a rematch up to <c>Created</c>, as its host's client does once the room exists.</summary>
    private async Task ConfirmAsync(Guid hostId, Guid sessionId)
    {
        await using var context = _fixture.CreateContext();
        var confirmed = await MultiplayerTest.Sessions(context).StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest());
        Assert.Equal(MultiplayerSessionState.Created, confirmed.Value!.State);
    }

    [Fact]
    public async Task The_first_to_ask_hosts_it_and_everyone_after_is_handed_the_same_room()
    {
        var match = await PlayedMatchAsync();

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        var first = await sessions.RematchAsync(match.GuestId, match.SessionId, Ask());

        Assert.True(first.Succeeded, first.Error?.Code);
        var rematch = first.Value!;
        Assert.Equal(match.GuestId, rematch.HostUserId);
        Assert.Equal(MultiplayerSessionState.Creating, rematch.State);
        Assert.Equal(SessionVisibility.Private, rematch.Visibility);
        Assert.False(rematch.IsRanked);
        Assert.Equal(match.GameId, rematch.GameId);

        var second = await sessions.RematchAsync(match.HostId, match.SessionId, Ask());

        Assert.Equal(rematch.Id, second.Value!.Id);

        // Handed the room, not put in it: it may not be up yet, and joining is the ordinary join.
        Assert.DoesNotContain(second.Value.Players, p => p.UserId == match.HostId);

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, rematch.Id);
        Assert.Equal(match.SessionId, stored.RematchOfSessionId);
        Assert.True(stored.IsReserved);
    }

    [Fact]
    public async Task Only_the_players_of_the_match_can_take_a_seat()
    {
        var match = await PlayedMatchAsync();

        await using var context = _fixture.CreateContext();
        var rematch = (await MultiplayerTest.Sessions(context).RematchAsync(match.GuestId, match.SessionId, Ask())).Value!;
        await ConfirmAsync(match.GuestId, rematch.Id);

        await using var strangers = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(strangers);
        var sessions = MultiplayerTest.Sessions(strangers);

        // A stranger holding the room's id, or its code, still does not get a seat meant for the
        // previous opponent.
        Assert.Equal("SESSION_RESERVED", (await sessions.JoinAsync(strangerId, rematch.Id, Join())).Error?.Code);
        Assert.Equal("SESSION_RESERVED", (await sessions.JoinByCodeAsync(
            strangerId, new JoinMultiplayerSessionByCodeRequest { JoinCode = rematch.JoinCode!, ProtocolVersion = 1 })).Error?.Code);

        var back = await sessions.JoinAsync(match.HostId, rematch.Id, Join());

        Assert.True(back.Succeeded, back.Error?.Code);
        Assert.Equal(2, back.Value!.CurrentPlayerCount);
    }

    [Fact]
    public async Task A_player_waiting_for_the_rematch_can_watch_it_come_up_and_nobody_else_can()
    {
        var match = await PlayedMatchAsync();

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);
        var rematch = (await sessions.RematchAsync(match.GuestId, match.SessionId, Ask())).Value!;

        var watched = await sessions.GetAsync(match.HostId, rematch.Id);
        Assert.True(watched.Succeeded, watched.Error?.Code);

        var strangerId = await TestData.CreateUserAsync(context);
        Assert.Equal("SESSION_NOT_FOUND", (await sessions.GetAsync(strangerId, rematch.Id)).Error?.Code);
    }

    [Fact]
    public async Task With_join_codes_required_a_reserved_player_still_joins_by_id()
    {
        var match = await PlayedMatchAsync();

        await using var context = _fixture.CreateContext();
        var rematch = (await MultiplayerTest.Sessions(context).RematchAsync(match.GuestId, match.SessionId, Ask())).Value!;
        await ConfirmAsync(match.GuestId, rematch.Id);

        var strict = MultiplayerTest.Sessions(context, MultiplayerTest.Options(o => o.RequireJoinCodeForPrivateSessions = true));

        // The rematch answer handed them this id; the code rollout must not strand them.
        var joined = await strict.JoinAsync(match.HostId, rematch.Id, Join());

        Assert.True(joined.Succeeded, joined.Error?.Code);
    }

    [Fact]
    public async Task A_match_still_running_or_never_played_has_no_rematch()
    {
        var running = await MultiplayerTest.OpenAsync(_fixture);
        var lobby = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        await sessions.StartAsync(running.HostId, running.SessionId, new StartMultiplayerSessionRequest());
        await sessions.CloseAsync(lobby.HostId, lobby.SessionId, new CloseMultiplayerSessionRequest());

        Assert.Equal("SESSION_INVALID_TRANSITION", (await sessions.RematchAsync(running.HostId, running.SessionId, Ask())).Error?.Code);
        Assert.Equal("SESSION_INVALID_TRANSITION", (await sessions.RematchAsync(lobby.HostId, lobby.SessionId, Ask())).Error?.Code);
    }

    [Fact]
    public async Task Someone_who_left_before_kick_off_did_not_play_and_cannot_reopen_it()
    {
        var open = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 3);
        var guestId = await MultiplayerTest.JoinAsync(_fixture, open.SessionId);
        var quitterId = await MultiplayerTest.JoinAsync(_fixture, open.SessionId);

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        await sessions.LeaveAsync(quitterId, open.SessionId, new LeaveMultiplayerSessionRequest());
        await sessions.StartAsync(open.HostId, open.SessionId, new StartMultiplayerSessionRequest());
        await sessions.CloseAsync(open.HostId, open.SessionId, new CloseMultiplayerSessionRequest());

        Assert.Equal("NOT_SESSION_MEMBER", (await sessions.RematchAsync(quitterId, open.SessionId, Ask())).Error?.Code);

        var rematch = (await sessions.RematchAsync(guestId, open.SessionId, Ask())).Value!;
        await ConfirmAsync(guestId, rematch.Id);

        Assert.Equal("SESSION_RESERVED", (await sessions.JoinAsync(quitterId, rematch.Id, Join())).Error?.Code);
    }

    [Fact]
    public async Task A_retried_request_is_answered_with_the_room_it_already_made()
    {
        var match = await PlayedMatchAsync();

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);

        var first = await sessions.RematchAsync(match.GuestId, match.SessionId, Ask("rematch-retry"));
        var retry = await sessions.RematchAsync(match.GuestId, match.SessionId, Ask("rematch-retry"));

        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Equal(first.Value.TransportSessionName, retry.Value.TransportSessionName);
    }

    [Fact]
    public async Task Two_players_pressing_rematch_at_once_land_in_one_room()
    {
        var match = await PlayedMatchAsync();

        // The host's rematch commits in the instant between the guest's checks and the guest's insert.
        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            var hosted = await MultiplayerTest.Sessions(other).RematchAsync(match.HostId, match.SessionId, Ask());
            Assert.True(hosted.Succeeded, hosted.Error?.Code);
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var guests = await MultiplayerTest.Sessions(context).RematchAsync(match.GuestId, match.SessionId, Ask());

        Assert.True(race.Fired);
        Assert.True(guests.Succeeded, guests.Error?.Code);
        Assert.Equal(match.HostId, guests.Value!.HostUserId);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessions.CountAsync(s => s.RematchOfSessionId == match.SessionId));
    }

    [Fact]
    public async Task A_rematch_whose_room_never_came_up_does_not_spend_the_match_s_one_chance()
    {
        var match = await PlayedMatchAsync();

        await using var context = _fixture.CreateContext();
        var sessions = MultiplayerTest.Sessions(context);
        var abandoned = (await sessions.RematchAsync(match.GuestId, match.SessionId, Ask())).Value!;

        // The sweeper fails a session stuck in Creating; done here directly.
        await context.MultiplayerSessions
            .Where(s => s.Id == abandoned.Id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.State, MultiplayerSessionState.Failed)
                .SetProperty(s => s.EndedAtUtc, DateTime.UtcNow));
        await context.MultiplayerSessionPlayers
            .Where(p => p.SessionId == abandoned.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, SessionPlayerStatus.Left));

        var again = await sessions.RematchAsync(match.HostId, match.SessionId, Ask());

        Assert.True(again.Succeeded, again.Error?.Code);
        Assert.NotEqual(abandoned.Id, again.Value!.Id);
        Assert.Equal(match.HostId, again.Value.HostUserId);
    }
}
