using Microsoft.EntityFrameworkCore;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The janitor.
/// <para>
/// **These are the tests for the only thing that makes a crashed host recoverable.** Every failure
/// mode in this domain ends with rows nobody will come back to close, and without a working sweep
/// those sessions hold their transport names forever while their members stay locked out of joining
/// anything else.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerSweeperTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerSweeperTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_session_whose_host_stopped_heartbeating_is_abandoned()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();
        await MultiplayerTest.AgeSessionAsync(context, session.SessionId, seconds: 300);

        var result = await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.Equal(1, result.Abandoned);

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);

        Assert.Equal(MultiplayerSessionState.Abandoned, stored.State);
        Assert.Equal(SessionClosedReason.Abandoned, stored.ClosedReason);
        Assert.NotNull(stored.EndedAtUtc);
        Assert.Equal(0, stored.CurrentPlayerCount);

        // **Memberships have to go too.** One account plays one match at a time, and that rule reads
        // membership rather than session state — leaving these seated would lock both players out of
        // every future match.
        var players = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);
        Assert.Equal(2, players.Count);
        Assert.All(players, p => Assert.Equal(SessionPlayerStatus.Left, p.Status));

        // Which is exactly what this proves: the joiner can start something else immediately.
        await using var next = _fixture.CreateContext();
        var fresh = await TestData.CreateCurriculumPathAsync(next);
        var restart = await MultiplayerTest.Sessions(next)
            .CreateAsync(joinerId, MultiplayerTest.CreateRequest(fresh.GameId));

        Assert.True(restart.Succeeded);
    }

    [Fact]
    public async Task A_session_stuck_in_creating_is_failed_with_creation_failed()
    {
        await using var context = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        // Created but never confirmed — the Photon room never came up.
        var created = await MultiplayerTest.Sessions(context)
            .CreateAsync(hostId, MultiplayerTest.CreateRequest(curriculum.GameId));

        Assert.Equal(MultiplayerSessionState.Creating, created.Value!.State);

        await MultiplayerTest.AgeSessionAsync(context, created.Value.Id, seconds: 60);

        var result = await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.Equal(1, result.FailedCreating);

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, created.Value.Id);

        // CreationFailed rather than Abandoned. Both are true of this row, but only one tells a
        // support engineer that the transport never came up.
        Assert.Equal(MultiplayerSessionState.Failed, stored.State);
        Assert.Equal(SessionClosedReason.CreationFailed, stored.ClosedReason);
    }

    [Fact]
    public async Task A_failed_session_releases_its_transport_name()
    {
        await using var context = _fixture.CreateContext();

        var curriculum = await TestData.CreateCurriculumPathAsync(context);
        var firstHost = await TestData.CreateUserAsync(context);
        var secondHost = await TestData.CreateUserAsync(context);

        const string name = "sweep_name_release";

        await MultiplayerTest.Sessions(context)
            .CreateAsync(firstHost, MultiplayerTest.CreateRequest(curriculum.GameId, name));

        var stuck = await MultiplayerTest.ReadSessionAsync(context, (await context.MultiplayerSessions
            .AsNoTracking().FirstAsync(s => s.TransportSessionName == name)).Id);

        await MultiplayerTest.AgeSessionAsync(context, stuck.Id, seconds: 60);
        await MultiplayerTest.Sweeper(context).SweepAsync();

        // The room name is a scarce, reusable resource. If a dead session kept it, a busy game would
        // slowly run out of names it could mint.
        await using var reuse = _fixture.CreateContext();
        var again = await MultiplayerTest.Sessions(reuse)
            .CreateAsync(secondHost, MultiplayerTest.CreateRequest(curriculum.GameId, name));

        Assert.True(again.Succeeded);
    }

    [Fact]
    public async Task A_member_disconnected_past_the_grace_period_loses_their_seat()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var context = _fixture.CreateContext();

        await MultiplayerTest.AgePlayerAsync(
            context, session.SessionId, joinerId, seconds: 300, status: SessionPlayerStatus.Disconnected);

        var result = await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.Equal(1, result.PlayersReleased);

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);

        // Recounted, not decremented. A count that drifts is how a session ends up looking full
        // while nobody is in it.
        Assert.Equal(1, stored.CurrentPlayerCount);
        Assert.Equal(MultiplayerSessionState.Created, stored.State);

        var released = Assert.Single(
            await MultiplayerTest.ReadPlayersAsync(check, session.SessionId),
            p => p.UserId == joinerId);

        Assert.Equal(SessionPlayerStatus.Left, released.Status);
    }

    [Fact]
    public async Task An_open_session_nobody_is_in_is_closed_as_empty()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();

        // The host itself times out of the roster. Releasing the last seat and closing the session
        // both have to happen, and the ordering inside one pass is what makes it happen now rather
        // than 30 seconds later.
        await MultiplayerTest.AgePlayerAsync(
            context, session.SessionId, session.HostId, seconds: 300, status: SessionPlayerStatus.Disconnected);

        var result = await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.Equal(1, result.PlayersReleased);
        Assert.Equal(1, result.ClosedEmpty);

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);

        Assert.Equal(MultiplayerSessionState.Closed, stored.State);
        Assert.Equal(SessionClosedReason.Empty, stored.ClosedReason);
    }

    [Fact]
    public async Task Expired_request_logs_are_purged_and_live_ones_are_not()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();

        // One recent entry, written by the create above; one aged past retention.
        context.MultiplayerRequestLogs.Add(new MultiplayerRequestLog
        {
            UserId = session.HostId,
            RequestId = "expired-key",
            Operation = "join",
            SessionId = session.SessionId,
            ResponseJson = "{}",
            StatusCode = 200,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-48)
        });

        await context.SaveChangesAsync();

        // Deliberately not asserting an exact count here — opening the session writes log entries of
        // its own, and pinning that number would make this test fail whenever the fixture changes
        // shape rather than when retention breaks.
        var before = await context.MultiplayerRequestLogs.CountAsync(l => l.UserId == session.HostId);
        Assert.True(before > 1);

        var result = await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.Equal(1, result.RequestLogsPurged);

        await using var check = _fixture.CreateContext();
        var remaining = await check.MultiplayerRequestLogs
            .Where(l => l.UserId == session.HostId)
            .ToListAsync();

        Assert.Equal(before - 1, remaining.Count);
        Assert.DoesNotContain(remaining, l => l.RequestId == "expired-key");
    }

    [Fact]
    public async Task A_second_pass_over_the_same_data_does_nothing()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        await MultiplayerTest.AgeSessionAsync(context, session.SessionId, seconds: 300);

        var first = await MultiplayerTest.Sweeper(context).SweepAsync();
        Assert.True(first.Total > 0);

        // **Idempotence is what makes overlapping runs across instances harmless**, and it is also
        // what lets a backlog drain over several passes instead of one enormous transaction.
        var second = await MultiplayerTest.Sweeper(context).SweepAsync();
        Assert.Equal(0, second.Total);
    }

    [Fact]
    public async Task A_healthy_session_is_left_alone()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var result = await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.Equal(0, result.Abandoned);
        Assert.Equal(0, result.FailedCreating);

        var stored = await MultiplayerTest.ReadSessionAsync(context, session.SessionId);
        Assert.Equal(MultiplayerSessionState.Created, stored.State);
    }
}
