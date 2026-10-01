using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Entities;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Races and invariants the lifecycle tests cannot see, each one forced deterministically with an
/// <see cref="InterleavingInterceptor"/> rather than left to the scheduler.
/// <para>
/// **Every test here was written against a real defect and failed before its fix.** The shape they
/// share: a host heartbeats four times a minute and a join touches the session row, so any operation
/// that reads a session, decides, and then writes it back guarded on the row version it read will
/// eventually lose to one of them — and several did so silently, answering 200 for a leave or a
/// close that never happened.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerRaceTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerRaceTests(SqlServerFixture fixture) => _fixture = fixture;

    private static HeartbeatRequest Beat(params Guid[] connected) => new() { ConnectedUserIds = [.. connected] };

    // -----------------------------------------------------------------------------------------
    // lifecycle moves racing a heartbeat
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_leave_that_races_a_heartbeat_still_releases_the_seat()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        var race = new InterleavingInterceptor(() => _fixture.SimulateHeartbeatCommitAsync(session.SessionId));
        await using var context = _fixture.CreateInterleavedContext(race);

        var left = await MultiplayerTest.Sessions(context)
            .LeaveAsync(joinerId, session.SessionId, new LeaveMultiplayerSessionRequest());

        Assert.True(race.Fired);
        Assert.True(left.Succeeded);

        // **The caller was told they left, so they must have.** Before the fix this answered 200 and
        // left the seat held: the heartbeat moved the row version, the leave's write matched nothing,
        // and the concurrency exception was read as "somebody else already did it".
        await using var check = _fixture.CreateContext();
        var players = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);
        Assert.DoesNotContain(players, p => p.UserId == joinerId && !p.Status.HasDeparted());

        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);
        Assert.Equal(1, stored.CurrentPlayerCount);
    }

    [Fact]
    public async Task A_close_that_races_a_heartbeat_still_closes_the_session()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        var race = new InterleavingInterceptor(() => _fixture.SimulateHeartbeatCommitAsync(session.SessionId));
        await using var context = _fixture.CreateInterleavedContext(race);

        var closed = await MultiplayerTest.Sessions(context)
            .CloseAsync(session.HostId, session.SessionId, new CloseMultiplayerSessionRequest());

        Assert.True(race.Fired);
        Assert.True(closed.Succeeded);
        Assert.Equal(MultiplayerSessionState.Closed, closed.Value!.State);

        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);
        Assert.Equal(MultiplayerSessionState.Closed, stored.State);

        // A terminal session with members still seated locks those accounts out of every future match.
        var players = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);
        Assert.All(players, p => Assert.True(p.Status.HasDeparted()));
    }

    [Fact]
    public async Task A_start_that_races_a_heartbeat_is_not_refused()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        var race = new InterleavingInterceptor(() => _fixture.SimulateHeartbeatCommitAsync(session.SessionId));
        await using var context = _fixture.CreateInterleavedContext(race);

        var started = await MultiplayerTest.Sessions(context)
            .StartAsync(session.HostId, session.SessionId, new StartMultiplayerSessionRequest());

        // A heartbeat changes nothing a start depends on. Refusing it with "the session moved" sends
        // the host round a retry loop for a conflict that never existed.
        Assert.True(race.Fired);
        Assert.True(started.Succeeded, started.Error?.Code);
        Assert.Equal(MultiplayerSessionState.Running, started.Value!.State);
    }

    [Fact]
    public async Task A_voluntary_host_transfer_that_races_a_heartbeat_is_not_refused()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var joinerId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        var race = new InterleavingInterceptor(() => _fixture.SimulateHeartbeatCommitAsync(session.SessionId));
        await using var context = _fixture.CreateInterleavedContext(race);

        var moved = await MultiplayerTest.Sessions(context).TransferHostAsync(
            session.HostId, session.SessionId, new TransferHostRequest { ToUserId = joinerId });

        // Before the fix this was answered HOST_STILL_ACTIVE, "another claim reached the session
        // first" — when no other claim existed at all.
        Assert.True(race.Fired);
        Assert.True(moved.Succeeded, moved.Error?.Code);
        Assert.Equal(joinerId, moved.Value!.HostUserId);

        await using var check = _fixture.CreateContext();
        var players = await MultiplayerTest.ReadPlayersAsync(check, session.SessionId);
        Assert.Single(players, p => p.IsHost);
        Assert.True(players.Single(p => p.UserId == joinerId).IsHost);
    }

    // -----------------------------------------------------------------------------------------
    // one account, one live seat
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task One_account_cannot_be_seated_in_two_sessions_by_overlapping_joins()
    {
        var first = await MultiplayerTest.OpenAsync(_fixture);
        var second = await MultiplayerTest.OpenAsync(_fixture);

        await using var setup = _fixture.CreateContext();
        var playerId = await TestData.CreateUserAsync(setup);

        // The second join runs start to finish inside the first one's window — after the first has
        // checked "is this account already seated?" and before it seats them.
        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            var joined = await MultiplayerTest.Sessions(other).JoinAsync(
                playerId, second.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });
            Assert.True(joined.Succeeded);
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var result = await MultiplayerTest.Sessions(context).JoinAsync(
            playerId, first.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.True(race.Fired);
        Assert.False(result.Succeeded);
        Assert.Equal("ALREADY_IN_SESSION", result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await SeatedCountAsync(check, playerId));

        // The refused join released the capacity it took, in the same transaction.
        var firstRow = await MultiplayerTest.ReadSessionAsync(check, first.SessionId);
        Assert.Equal(1, firstRow.CurrentPlayerCount);
    }

    [Fact]
    public async Task One_account_cannot_host_two_sessions_by_overlapping_creates()
    {
        await using var setup = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(setup);
        var hostId = await TestData.CreateUserAsync(setup);

        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            var created = await MultiplayerTest.Sessions(other)
                .CreateAsync(hostId, MultiplayerTest.CreateRequest(curriculum.GameId));
            Assert.True(created.Succeeded);
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var result = await MultiplayerTest.Sessions(context)
            .CreateAsync(hostId, MultiplayerTest.CreateRequest(curriculum.GameId));

        Assert.True(race.Fired);
        Assert.False(result.Succeeded);
        Assert.Equal("ALREADY_IN_SESSION", result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await SeatedCountAsync(check, hostId));
        Assert.Equal(1, await check.MultiplayerSessions.CountAsync(s => s.HostUserId == hostId));
    }

    // -----------------------------------------------------------------------------------------
    // a retry that overlaps its own first attempt
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_matchmake_retried_while_its_first_attempt_is_still_running_gets_the_same_answer()
    {
        await using var setup = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(setup);
        var playerId = await TestData.CreateUserAsync(setup);

        // A genuine retry: same key, same body, room name included — the phone resends what it sent.
        var request = new MatchmakeRequest
        {
            GameId = curriculum.GameId,
            ProtocolVersion = 1,
            CurriculumPath = new CurriculumPathDto { LessonId = curriculum.LessonId },
            CreateIfNoneFound = true,
            TransportSessionName = MultiplayerTest.NewTransportName(),
            RequestId = $"retry_{Guid.NewGuid():N}"
        };

        MatchmakeResponse? original = null;

        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            var first = await MultiplayerTest.Matchmaking(other).MatchmakeAsync(playerId, request);
            Assert.True(first.Succeeded, first.Error?.Code);
            original = first.Value;
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var retried = await MultiplayerTest.Matchmaking(context).MatchmakeAsync(playerId, request);

        // The retry lost the race to its own original. It is owed the original's answer — the same
        // session and the same outcome — not ALREADY_IN_SESSION or TRANSPORT_NAME_TAKEN caused by its twin.
        Assert.True(race.Fired);
        Assert.True(retried.Succeeded, retried.Error?.Code);
        Assert.Equal(original!.Outcome, retried.Value!.Outcome);
        Assert.Equal(original.Session!.Id, retried.Value.Session!.Id);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await SeatedCountAsync(check, playerId));
        Assert.Equal(1, await check.MultiplayerSessions.CountAsync(s => s.HostUserId == playerId));
    }

    [Fact]
    public async Task A_create_retried_while_its_first_attempt_is_still_running_gets_the_same_session()
    {
        await using var setup = _fixture.CreateContext();
        var curriculum = await TestData.CreateCurriculumPathAsync(setup);
        var hostId = await TestData.CreateUserAsync(setup);

        var request = MultiplayerTest.CreateRequest(curriculum.GameId, requestId: $"retry_{Guid.NewGuid():N}");

        MultiplayerSessionDto? original = null;

        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            var first = await MultiplayerTest.Sessions(other).CreateAsync(hostId, request);
            Assert.True(first.Succeeded, first.Error?.Code);
            original = first.Value;
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var retried = await MultiplayerTest.Sessions(context).CreateAsync(hostId, request);

        Assert.True(race.Fired);
        Assert.True(retried.Succeeded, retried.Error?.Code);
        Assert.Equal(original!.Id, retried.Value!.Id);
    }

    [Fact]
    public async Task Leaving_after_rejoining_releases_the_seat_held_now()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 4);

        await using var setup = _fixture.CreateContext();
        var playerId = await TestData.CreateUserAsync(setup);

        // Several rounds, because the defect was an unordered read of "the caller's membership":
        // with a departed row and a live one both present, which one came back was up to the plan.
        for (var round = 0; round < 4; round++)
        {
            await using var context = _fixture.CreateContext();
            var sessions = MultiplayerTest.Sessions(context);

            var joined = await sessions.JoinAsync(
                playerId, session.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });
            Assert.True(joined.Succeeded, joined.Error?.Code);

            var left = await sessions.LeaveAsync(playerId, session.SessionId, new LeaveMultiplayerSessionRequest());
            Assert.True(left.Succeeded);

            await using var check = _fixture.CreateContext();
            Assert.Equal(0, await SeatedCountAsync(check, playerId));
        }
    }

    // -----------------------------------------------------------------------------------------
    // the janitor
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_player_seated_but_never_seen_on_the_transport_is_eventually_released()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);
        var ghostId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        // Seated by the backend, never reported by the host: their app died between the join
        // response and connecting to the room. Status stays Joined.
        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgePlayerAsync(aging, session.SessionId, ghostId, seconds: 120);

        await using var context = _fixture.CreateContext();
        var beat = await MultiplayerTest.Sessions(context)
            .HeartbeatAsync(session.HostId, session.SessionId, Beat(session.HostId));
        Assert.True(beat.Succeeded);

        await MultiplayerTest.Sweeper(context).SweepAsync();

        // Before the fix the heartbeat only ever demoted Connected members, so a member who never
        // connected held their seat until the whole session ended — and in a two-seat game the host
        // was shown a full lobby with an opponent who was never coming.
        await using var check = _fixture.CreateContext();
        var ghost = (await MultiplayerTest.ReadPlayersAsync(check, session.SessionId)).Single(p => p.UserId == ghostId);
        Assert.True(ghost.Status.HasDeparted());

        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);
        Assert.Equal(1, stored.CurrentPlayerCount);
    }

    [Fact]
    public async Task The_sweeper_recount_keeps_a_seat_taken_while_it_runs()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture, maxPlayers: 3);
        var staleId = await MultiplayerTest.JoinAsync(_fixture, session.SessionId);

        await using var aging = _fixture.CreateContext();
        await MultiplayerTest.AgePlayerAsync(
            aging, session.SessionId, staleId, seconds: 300, status: SessionPlayerStatus.Disconnected);

        var newcomerId = await TestData.CreateUserAsync(aging);

        // A join commits just before the sweeper writes this session's recount.
        var race = new InterleavingInterceptor(
            async () =>
            {
                await using var other = _fixture.CreateContext();
                var joined = await MultiplayerTest.Sessions(other).JoinAsync(
                    newcomerId, session.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });
                Assert.True(joined.Succeeded, joined.Error?.Code);
            },
            command => command.CommandText.Contains("CurrentPlayerCount", StringComparison.Ordinal)
                       && !command.CommandText.Contains("[State]", StringComparison.Ordinal)
                       && InterleavingInterceptor.IsWrite(command.CommandText)
                       && InterleavingInterceptor.HasParameter(command, session.SessionId));

        await using var context = _fixture.CreateInterleavedContext(race);
        await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.True(race.Fired);

        // **The count must equal the seats.** A count below the seats is a session that admits one
        // player more than it has room for.
        await using var check = _fixture.CreateContext();
        var stored = await MultiplayerTest.ReadSessionAsync(check, session.SessionId);
        var seated = (await MultiplayerTest.ReadPlayersAsync(check, session.SessionId)).Count(p => !p.Status.HasDeparted());

        Assert.Equal(2, seated);
        Assert.Equal(seated, stored.CurrentPlayerCount);
    }

    // -----------------------------------------------------------------------------------------
    // child safety
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Players_matched_together_see_a_generated_handle_rather_than_a_real_name()
    {
        var session = await MultiplayerTest.OpenAsync(_fixture);

        await using var setup = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(setup);

        setup.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            UserId = joinerId,
            FullName = "Layla Hassan",
            Age = 9,
            GradeId = await setup.Grades.Select(g => g.Id).FirstAsync(),
            CreatedAt = DateTime.UtcNow
        });
        await setup.SaveChangesAsync();

        await using var context = _fixture.CreateContext();
        var joined = await MultiplayerTest.Sessions(context).JoinAsync(
            joinerId, session.SessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });
        Assert.True(joined.Succeeded);

        await using var hostView = _fixture.CreateContext();
        var seen = await MultiplayerTest.Sessions(hostView).GetAsync(session.HostId, session.SessionId);

        // A public match seats strangers together. The name on the seat is the platform's generated
        // handle — the same rule the leaderboards already enforce — never a child's real name.
        var seat = Assert.Single(seen.Value!.Players, p => p.UserId == joinerId);
        Assert.NotNull(seat.DisplayName);
        Assert.DoesNotContain("Layla", seat.DisplayName);

        await using var check = _fixture.CreateContext();
        var handle = await check.PlayerDisplayNames.Where(n => n.UserId == joinerId).Select(n => n.Handle).SingleAsync();
        Assert.Equal(handle, seat.DisplayName);
    }

    private static Task<int> SeatedCountAsync(Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid userId) =>
        context.MultiplayerSessionPlayers.CountAsync(p =>
            p.UserId == userId
            && p.Status != SessionPlayerStatus.Left
            && p.Status != SessionPlayerStatus.Removed);
}
