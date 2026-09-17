using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The host check-in: what keeps a session alive, and how the server reconciles the host's view of
/// who is present against its own.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerHeartbeatTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerHeartbeatTests(SqlServerFixture fixture) => _fixture = fixture;

    private static HeartbeatRequest Beat(params Guid[] connected) =>
        new() { ConnectedUserIds = [.. connected] };

    [Fact]
    public async Task A_heartbeat_advances_the_session_clock_from_the_server()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgeSessionAsync(aging, session.SessionId, seconds: 40);

        var before = await MultiplayerTest.ReadSessionAsync(aging, session.SessionId);

        await using var context = _fixture.CreateContext();
        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(session.HostId, session.SessionId, Beat(session.HostId));

        Assert.True(beat.Succeeded);
        Assert.Equal(MultiplayerSessionState.Created, beat.Value!.State);
        Assert.Equal(15, beat.Value.NextHeartbeatInSeconds);

        await using var check = _fixture.CreateContext();
        var after = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);

        Assert.True(after.LastHeartbeatAtUtc > before.LastHeartbeatAtUtc);
    }

    [Fact]
    public async Task A_reported_member_is_marked_connected_and_seen()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(session.HostId, session.SessionId, Beat(session.HostId, joinerId));

        Assert.True(beat.Succeeded);
        Assert.All(beat.Value!.Players, p => Assert.Equal(SessionPlayerStatus.Connected, p.Status));
    }

    [Fact]
    public async Task A_user_the_host_names_but_who_is_not_a_member_is_ignored()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        // **The roster asserts presence, never membership.** If this seated anybody, a modified
        // client could add arbitrary accounts to a session just by naming them.
        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(session.HostId, session.SessionId, Beat(session.HostId, strangerId));

        Assert.True(beat.Succeeded);
        Assert.DoesNotContain(beat.Value!.Players, p => p.UserId == strangerId);

        await using var check = _fixture.CreateContext();
        var players = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);

        Assert.Single(players);
        Assert.Equal(session.HostId, players[0].UserId);
    }

    [Fact]
    public async Task A_member_missing_past_the_grace_period_becomes_disconnected_but_keeps_their_seat()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var aging = _fixture.CreateContext();

        // Seen once, then absent for longer than the grace period.
        await MultiplayerTest.AgePlayerAsync(
            aging, session.SessionId, joinerId, seconds: 120, status: SessionPlayerStatus.Connected);

        await using var context = _fixture.CreateContext();
        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(session.HostId, session.SessionId, Beat(session.HostId));

        Assert.True(beat.Succeeded);

        var absent = Assert.Single(beat.Value!.Players, p => p.UserId == joinerId);
        Assert.Equal(SessionPlayerStatus.Disconnected, absent.Status);

        // **Still seated.** Only the sweeper ever promotes this to Left — a host that briefly cannot
        // see a peer must not be able to evict them.
        await using var check = _fixture.CreateContext();
        var session2 = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);
        Assert.Equal(2, session2.CurrentPlayerCount);
    }

    [Fact]
    public async Task A_member_who_is_not_the_host_cannot_heartbeat()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(joinerId, session.SessionId, Beat(joinerId));

        Assert.False(beat.Succeeded);
        Assert.Equal(ServiceErrorKind.Forbidden, beat.ErrorKind);
        Assert.Equal("NOT_SESSION_HOST", beat.Error!.Code);
    }

    [Fact]
    public async Task A_stranger_gets_not_found_rather_than_forbidden()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(strangerId, session.SessionId, Beat(strangerId));

        Assert.Equal(ServiceErrorKind.NotFound, beat.ErrorKind);
    }

    [Fact]
    public async Task A_heartbeat_reports_a_swept_session_without_resurrecting_it()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgeSessionAsync(aging, session.SessionId, seconds: 300);
        await MultiplayerTest.Sweeper(aging).SweepAsync();

        await using var context = _fixture.CreateContext();
        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(session.HostId, session.SessionId, Beat(session.HostId));

        // The host learns the truth and is expected to tear down. A late heartbeat must not bring a
        // terminal session back — the seats have already been released to their owners.
        Assert.True(beat.Succeeded);
        Assert.Equal(MultiplayerSessionState.Abandoned, beat.Value!.State);

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);
        Assert.Equal(MultiplayerSessionState.Abandoned, stored.State);
    }
}
