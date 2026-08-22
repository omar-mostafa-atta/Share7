using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The session lifecycle, against a real SQL Server.
/// <para>
/// **The provider matters more here than anywhere else in the suite.** Almost everything worth
/// asserting below is enforced by the database rather than by C#: a conditional UPDATE deciding the
/// last seat, and filtered unique indexes refusing a double-join or a duplicate room. An in-memory
/// provider has neither, so it would pass every one of these while production failed them.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MultiplayerSessionServiceTests
{
    private readonly SqlServerFixture _fixture;

    public MultiplayerSessionServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private static MultiplayerSessionService Service(ApplicationDbContext context, params int[] accepted) =>
        new(
            context,
            new MultiplayerRequestLogStore(context),
            Options.Create(new MultiplayerOptions
            {
                AcceptedProtocolVersions = accepted.Length == 0 ? [1] : [.. accepted]
            }));

    private static CreateMultiplayerSessionRequest CreateRequest(
        Guid gameId,
        string? transportName = null,
        int protocolVersion = 1,
        string? requestId = null,
        SessionVisibility? visibility = null,
        CurriculumPathDto? path = null) =>
        new()
        {
            GameId = gameId,
            TransportSessionName = transportName ?? $"room_{Guid.NewGuid():N}"[..24],
            ProtocolVersion = protocolVersion,
            RequestId = requestId,
            Visibility = visibility,
            CurriculumPath = path
        };

    /// <summary>A host with a session already confirmed up to <c>Created</c>, so it accepts joins.</summary>
    private async Task<(Guid HostId, Guid SessionId, Guid GameId)> OpenSessionAsync(string? transportName = null)
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        var created = await Service(context).CreateAsync(hostId, CreateRequest(fixture.GameId, transportName));
        Assert.True(created.Succeeded);

        var confirmed = await Service(context).StartAsync(
            hostId, created.Value!.Id, new StartMultiplayerSessionRequest());

        Assert.True(confirmed.Succeeded);
        Assert.Equal(MultiplayerSessionState.Created, confirmed.Value!.State);

        return (hostId, created.Value.Id, fixture.GameId);
    }

    // -----------------------------------------------------------------------------------------
    // creation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_seats_the_host_and_starts_in_creating()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        var result = await Service(context).CreateAsync(hostId, CreateRequest(fixture.GameId));

        Assert.True(result.Succeeded);

        var session = result.Value!;
        Assert.Equal(MultiplayerSessionState.Creating, session.State);
        Assert.Equal(hostId, session.HostUserId);
        Assert.Equal(1, session.CurrentPlayerCount);

        var host = Assert.Single(session.Players);
        Assert.Equal(hostId, host.UserId);
        Assert.Equal(0, host.Slot);
        Assert.True(host.IsHost);

        // A session that exists without its host would be unrecoverable — nobody could start or
        // close it. The two rows are written in one transaction precisely so this cannot happen.
        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessionPlayers.CountAsync(p => p.SessionId == session.Id));
    }

    [Fact]
    public async Task Create_refuses_a_second_live_session_for_the_same_transport_name()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var firstHost = await TestData.CreateUserAsync(context);
        var secondHost = await TestData.CreateUserAsync(context);

        const string sharedName = "room_collision_test";

        var first = await Service(context).CreateAsync(firstHost, CreateRequest(fixture.GameId, sharedName));
        Assert.True(first.Succeeded);

        await using var second = _fixture.CreateContext();
        var clash = await Service(second).CreateAsync(secondHost, CreateRequest(fixture.GameId, sharedName));

        Assert.False(clash.Succeeded);
        Assert.Equal("TRANSPORT_NAME_TAKEN", clash.Error!.Code);
        Assert.Equal(ServiceErrorKind.Conflict, clash.ErrorKind);

        // **No orphan.** The session and its host row go in together, so a refused create leaves
        // neither behind.
        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessions.CountAsync(s => s.TransportSessionName == sharedName));
        Assert.Empty(await check.MultiplayerSessionPlayers.Where(p => p.UserId == secondHost).ToListAsync());
    }

    [Fact]
    public async Task Create_refuses_a_caller_who_is_already_in_a_session()
    {
        var (hostId, _, gameId) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var second = await Service(context).CreateAsync(hostId, CreateRequest(gameId));

        Assert.False(second.Succeeded);
        Assert.Equal("ALREADY_IN_SESSION", second.Error!.Code);
    }

    [Fact]
    public async Task Create_refuses_a_game_that_is_not_multiplayer()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        var game = await context.Games.FirstAsync(g => g.Id == fixture.GameId);
        game.SupportsMultiplayer = false;
        await context.SaveChangesAsync();

        var result = await Service(context).CreateAsync(hostId, CreateRequest(fixture.GameId));

        Assert.False(result.Succeeded);
        Assert.Equal("GAME_NOT_MULTIPLAYER", result.Error!.Code);
    }

    [Fact]
    public async Task Create_clamps_max_players_to_the_catalog()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        var request = CreateRequest(fixture.GameId);
        request.MaxPlayers = 99;

        var result = await Service(context).CreateAsync(hostId, request);

        Assert.True(result.Succeeded);

        // The catalog wins over the request. A client asking for more seats than the game allows
        // gets the game's number rather than a refusal.
        var game = await context.Games.AsNoTracking().FirstAsync(g => g.Id == fixture.GameId);
        Assert.Equal(game.MaxPlayers, result.Value!.MaxPlayers);
    }

    [Fact]
    public async Task Private_session_gets_a_join_code_and_a_public_one_does_not()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var privateHost = await TestData.CreateUserAsync(context);
        var publicHost = await TestData.CreateUserAsync(context);

        var privateSession = await Service(context).CreateAsync(
            privateHost, CreateRequest(fixture.GameId, visibility: SessionVisibility.Private));

        var publicSession = await Service(context).CreateAsync(
            publicHost, CreateRequest(fixture.GameId));

        Assert.False(string.IsNullOrWhiteSpace(privateSession.Value!.JoinCode));
        Assert.Null(publicSession.Value!.JoinCode);
    }

    [Fact]
    public async Task Curriculum_path_is_echoed_back_and_its_lesson_is_queryable()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        var path = new CurriculumPathDto
        {
            GradeId = fixture.GradeId,
            TermId = fixture.TermId,
            SubjectId = fixture.SubjectId,
            ChapterId = fixture.ChapterId,
            LessonId = fixture.LessonId
        };

        var result = await Service(context).CreateAsync(hostId, CreateRequest(fixture.GameId, path: path));

        Assert.True(result.Succeeded);
        Assert.Equal(fixture.LessonId, result.Value!.CurriculumPath!.LessonId);
        Assert.Equal(fixture.ChapterId, result.Value.CurriculumPath.ChapterId);

        // Lifted into its own column so matchmaking can filter on it with an index rather than by
        // parsing JSON.
        await using var check = _fixture.CreateContext();
        var stored = await check.MultiplayerSessions.AsNoTracking().FirstAsync(s => s.Id == result.Value.Id);
        Assert.Equal(fixture.LessonId, stored.LessonId);
    }

    [Fact]
    public async Task Every_timestamp_on_the_wire_is_marked_utc()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        await Service(context).StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest());
        await Service(context).CloseAsync(hostId, sessionId, new CloseMultiplayerSessionRequest());

        var read = await Service(context).GetAsync(hostId, sessionId);
        var session = read.Value!;

        // **These come back from datetime2, which carries no timezone**, so EF materialises them as
        // Unspecified and the serializer emits them with no Z — while serverTimeUtc, generated in
        // memory, keeps its Z. A naive DateTime.Parse then reads the unmarked ones as local time.
        // The client computes heartbeat drift by comparing the two, so in Cairo they would disagree
        // by three hours and every session would look wildly out of sync.
        Assert.Equal(DateTimeKind.Utc, session.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, session.ServerTimeUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, session.StartedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, session.EndedAtUtc!.Value.Kind);

        var roster = await Service(context).GetPlayersAsync(hostId, sessionId);

        Assert.All(roster.Value!, player =>
        {
            Assert.Equal(DateTimeKind.Utc, player.JoinedAtUtc.Kind);
            Assert.Equal(DateTimeKind.Utc, player.LastSeenAtUtc.Kind);
        });
    }

    [Fact]
    public async Task An_unjoinable_session_reports_why_in_details()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        await Service(context).CloseAsync(hostId, sessionId, new CloseMultiplayerSessionRequest());

        var joinerId = await TestData.CreateUserAsync(context);
        var join = await Service(context).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.False(join.Succeeded);
        Assert.Equal("SESSION_CLOSED", join.Error!.Code);

        // One code covers every unjoinable state, so the state has to travel in details — otherwise
        // "the match ended" and "the host never confirmed the room, so it was swept" are the same
        // opaque refusal.
        Assert.Equal("CLOSED", join.Details!["state"]);
        Assert.Equal("HOST_CLOSED", join.Details["closedReason"]);
    }

    // -----------------------------------------------------------------------------------------
    // the create saga: Creating -> Created -> Running
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_session_still_in_creating_cannot_be_joined()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);
        var joinerId = await TestData.CreateUserAsync(context);

        var created = await Service(context).CreateAsync(hostId, CreateRequest(fixture.GameId));

        // Nobody may be seated into a Photon room that has not been confirmed to exist.
        var join = await Service(context).JoinAsync(
            joinerId, created.Value!.Id, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.False(join.Succeeded);
        Assert.Equal("SESSION_CLOSED", join.Error!.Code);
    }

    [Fact]
    public async Task Start_moves_a_confirmed_session_to_running_and_sets_the_clock()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var started = await Service(context).StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest());

        Assert.True(started.Succeeded);
        Assert.Equal(MultiplayerSessionState.Running, started.Value!.State);
        Assert.NotNull(started.Value.StartedAtUtc);
    }

    [Fact]
    public async Task Start_is_refused_once_the_session_is_running()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        await Service(context).StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest());

        await using var again = _fixture.CreateContext();
        var third = await Service(again).StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest());

        Assert.False(third.Succeeded);
        Assert.Equal("SESSION_INVALID_TRANSITION", third.Error!.Code);
    }

    // -----------------------------------------------------------------------------------------
    // joining — capacity, duplicates, protocol
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_joins_never_oversubscribe_the_last_seat()
    {
        var (_, sessionId, _) = await OpenSessionAsync();

        // MaxPlayers is 2 and the host holds one seat, so exactly one of these can win.
        var contenders = new List<Guid>();

        await using (var setup = _fixture.CreateContext())
        {
            for (var i = 0; i < 8; i++)
                contenders.Add(await TestData.CreateUserAsync(setup));
        }

        // A context per racer. Sharing one would serialise the very race under test.
        var attempts = contenders.Select(async userId =>
        {
            await using var context = _fixture.CreateContext();
            return await Service(context).JoinAsync(
                userId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal("SESSION_FULL", r.Error!.Code));

        await using var check = _fixture.CreateContext();
        var session = await check.MultiplayerSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);

        Assert.Equal(session.MaxPlayers, session.CurrentPlayerCount);

        // The denormalised count and the membership rows must agree — a count that drifts is how a
        // session ends up unjoinable while looking empty.
        var seated = await check.MultiplayerSessionPlayers
            .CountAsync(p => p.SessionId == sessionId
                             && p.Status != SessionPlayerStatus.Left
                             && p.Status != SessionPlayerStatus.Removed);

        Assert.Equal(session.CurrentPlayerCount, seated);
    }

    [Fact]
    public async Task Joining_twice_is_refused_and_leaves_one_membership()
    {
        var (_, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(context);

        var first = await Service(context).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.True(first.Succeeded);

        await using var second = _fixture.CreateContext();
        var repeat = await Service(second).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.False(repeat.Succeeded);
        Assert.Equal("ALREADY_IN_SESSION", repeat.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessionPlayers
            .CountAsync(p => p.SessionId == sessionId && p.UserId == joinerId));
    }

    [Fact]
    public async Task Joining_a_closed_session_is_refused()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        await Service(context).CloseAsync(hostId, sessionId, new CloseMultiplayerSessionRequest());

        await using var joining = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(joining);

        var join = await Service(joining).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        Assert.False(join.Succeeded);
        Assert.Equal("SESSION_CLOSED", join.Error!.Code);
    }

    [Fact]
    public async Task An_unaccepted_protocol_version_is_refused_and_seats_nobody()
    {
        var (_, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(context);

        var join = await Service(context).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 7 });

        Assert.False(join.Succeeded);
        Assert.Equal("PROTOCOL_VERSION_MISMATCH", join.Error!.Code);
        Assert.Equal(ServiceErrorKind.Validation, join.ErrorKind);

        await using var check = _fixture.CreateContext();
        Assert.Empty(await check.MultiplayerSessionPlayers.Where(p => p.UserId == joinerId).ToListAsync());
    }

    [Fact]
    public async Task An_accepted_version_that_the_session_does_not_run_is_still_refused()
    {
        var (_, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(context);

        // Version 2 is accepted by the server during a staged rollout, but this session is running
        // version 1 — putting the two builds in one room is exactly what the check exists to stop.
        var join = await Service(context, 1, 2).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 2 });

        Assert.False(join.Succeeded);
        Assert.Equal("PROTOCOL_VERSION_MISMATCH", join.Error!.Code);
    }

    // -----------------------------------------------------------------------------------------
    // leaving and closing — both idempotent
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Leaving_twice_succeeds_twice_and_decrements_once()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var joining = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(joining);
        await Service(joining).JoinAsync(joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        await using var first = _fixture.CreateContext();
        var left = await Service(first).LeaveAsync(joinerId, sessionId, new LeaveMultiplayerSessionRequest());
        Assert.True(left.Succeeded);

        await using var second = _fixture.CreateContext();
        var again = await Service(second).LeaveAsync(joinerId, sessionId, new LeaveMultiplayerSessionRequest());
        Assert.True(again.Succeeded);

        await using var check = _fixture.CreateContext();
        var session = await check.MultiplayerSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);

        // Back to the host alone. A second decrement would have made this 0 and closed the session
        // out from under a host who is still sitting in it.
        Assert.Equal(1, session.CurrentPlayerCount);
        Assert.Equal(hostId, session.HostUserId);

        var membership = await check.MultiplayerSessionPlayers
            .AsNoTracking()
            .FirstAsync(p => p.SessionId == sessionId && p.UserId == joinerId);

        Assert.Equal(SessionPlayerStatus.Left, membership.Status);
        Assert.NotNull(membership.LeftAtUtc);
    }

    [Fact]
    public async Task The_last_member_leaving_closes_the_session_as_empty()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var left = await Service(context).LeaveAsync(hostId, sessionId, new LeaveMultiplayerSessionRequest());

        Assert.True(left.Succeeded);
        Assert.Equal(MultiplayerSessionState.Closed, left.Value!.State);

        await using var check = _fixture.CreateContext();
        var session = await check.MultiplayerSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId);

        Assert.Equal(SessionClosedReason.Empty, session.ClosedReason);
        Assert.Equal(0, session.CurrentPlayerCount);
    }

    [Fact]
    public async Task A_host_who_leaves_hands_authority_to_the_lowest_remaining_seat()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var joining = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(joining);
        await Service(joining).JoinAsync(joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        await using var context = _fixture.CreateContext();
        var left = await Service(context).LeaveAsync(hostId, sessionId, new LeaveMultiplayerSessionRequest());

        Assert.True(left.Succeeded);
        Assert.Equal(joinerId, left.Value!.HostUserId);

        // The denormalised flag has to move with the session's own field, or the roster and the
        // session disagree about who is in charge.
        var successor = Assert.Single(left.Value.Players);
        Assert.True(successor.IsHost);
        Assert.Equal(joinerId, successor.UserId);
    }

    [Fact]
    public async Task Closing_twice_succeeds_twice_without_moving_the_end_time()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var first = _fixture.CreateContext();
        var closed = await Service(first).CloseAsync(
            hostId, sessionId, new CloseMultiplayerSessionRequest { RequestId = "close-one" });

        Assert.True(closed.Succeeded);
        var endedAt = closed.Value!.EndedAtUtc;
        Assert.NotNull(endedAt);

        // A *different* request id, so this exercises the absorbing terminal state rather than the
        // idempotency replay — the two are separate guarantees and both have to hold.
        await using var second = _fixture.CreateContext();
        var again = await Service(second).CloseAsync(
            hostId, sessionId, new CloseMultiplayerSessionRequest { RequestId = "close-two" });

        Assert.True(again.Succeeded);
        Assert.Equal(endedAt, again.Value!.EndedAtUtc);
        Assert.Equal(MultiplayerSessionState.Closed, again.Value.State);
    }

    [Fact]
    public async Task Closing_marks_every_remaining_membership_departed()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var joining = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(joining);
        await Service(joining).JoinAsync(joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        await using var context = _fixture.CreateContext();
        await Service(context).CloseAsync(hostId, sessionId, new CloseMultiplayerSessionRequest());

        await using var check = _fixture.CreateContext();
        var memberships = await check.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .ToListAsync();

        Assert.Equal(2, memberships.Count);
        Assert.All(memberships, m => Assert.Equal(SessionPlayerStatus.Left, m.Status));

        // Seats released, so both accounts can start or join something else immediately.
        await using var next = _fixture.CreateContext();
        var fresh = await TestData.CreateCurriculumPathAsync(next);
        var restart = await Service(next).CreateAsync(joinerId, CreateRequest(fresh.GameId));

        Assert.True(restart.Succeeded);
    }

    [Fact]
    public async Task A_member_can_still_read_a_session_after_it_has_closed()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        await Service(context).CloseAsync(hostId, sessionId, new CloseMultiplayerSessionRequest());

        // Closing marks every membership departed. Reading has to survive that, or a client can
        // never fetch the final state of the match it just played — and the host cannot re-close
        // the session it just closed, which is what makes close idempotent.
        await using var reading = _fixture.CreateContext();
        var read = await Service(reading).GetAsync(hostId, sessionId);

        Assert.True(read.Succeeded);
        Assert.Equal(MultiplayerSessionState.Closed, read.Value!.State);
    }

    // -----------------------------------------------------------------------------------------
    // authorization
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_non_member_gets_not_found_rather_than_forbidden()
    {
        var (_, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        var read = await Service(context).GetAsync(strangerId, sessionId);

        // 403 would confirm the id is real, which is enough to enumerate live sessions. A stranger
        // learns exactly as much as they are entitled to: nothing.
        Assert.False(read.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, read.ErrorKind);
        Assert.Equal("SESSION_NOT_FOUND", read.Error!.Code);

        var roster = await Service(context).GetPlayersAsync(strangerId, sessionId);
        Assert.Equal(ServiceErrorKind.NotFound, roster.ErrorKind);
    }

    [Fact]
    public async Task A_member_who_is_not_the_host_cannot_start_or_close()
    {
        var (_, sessionId, _) = await OpenSessionAsync();

        await using var joining = _fixture.CreateContext();
        var joinerId = await TestData.CreateUserAsync(joining);
        await Service(joining).JoinAsync(joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        await using var context = _fixture.CreateContext();

        var start = await Service(context).StartAsync(joinerId, sessionId, new StartMultiplayerSessionRequest());
        Assert.Equal(ServiceErrorKind.Forbidden, start.ErrorKind);
        Assert.Equal("NOT_SESSION_HOST", start.Error!.Code);

        var close = await Service(context).CloseAsync(joinerId, sessionId, new CloseMultiplayerSessionRequest());
        Assert.Equal(ServiceErrorKind.Forbidden, close.ErrorKind);
        Assert.Equal("NOT_SESSION_HOST", close.Error!.Code);
    }

    // -----------------------------------------------------------------------------------------
    // idempotency
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_replayed_create_returns_the_first_session_rather_than_a_second_one()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);

        var request = CreateRequest(fixture.GameId, requestId: "create-retry");

        var first = await Service(context).CreateAsync(hostId, request);
        Assert.True(first.Succeeded);

        // The same key, as a client retrying after a lost response would send.
        await using var retry = _fixture.CreateContext();
        var second = await Service(retry).CreateAsync(hostId, CreateRequest(
            fixture.GameId, request.TransportSessionName, requestId: "create-retry"));

        Assert.True(second.Succeeded);
        Assert.Equal(first.Value!.Id, second.Value!.Id);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.MultiplayerSessions.CountAsync(s => s.HostUserId == hostId));
    }

    [Fact]
    public async Task A_refused_join_does_not_burn_its_request_id()
    {
        var (_, sessionId, _) = await OpenSessionAsync();

        await using var setup = _fixture.CreateContext();
        var blockerId = await TestData.CreateUserAsync(setup);
        var joinerId = await TestData.CreateUserAsync(setup);

        // Fill the last seat.
        await Service(setup).JoinAsync(blockerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 });

        await using var refused = _fixture.CreateContext();
        var full = await Service(refused).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1, RequestId = "join-retry" });

        Assert.False(full.Succeeded);
        Assert.Equal("SESSION_FULL", full.Error!.Code);

        // A seat frees up.
        await using var leaving = _fixture.CreateContext();
        await Service(leaving).LeaveAsync(blockerId, sessionId, new LeaveMultiplayerSessionRequest());

        // **The same request id, retried.** This is what a well-behaved client does, and commerce
        // shipped the version of this bug where the stale refusal came back forever instead.
        await using var retry = _fixture.CreateContext();
        var seated = await Service(retry).JoinAsync(
            joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1, RequestId = "join-retry" });

        Assert.True(seated.Succeeded);
        Assert.Contains(seated.Value!.Players, p => p.UserId == joinerId);
    }

    [Fact]
    public async Task An_absent_request_id_is_not_an_error()
    {
        await using var context = _fixture.CreateContext();

        var fixture = await TestData.CreateCurriculumPathAsync(context);
        var hostId = await TestData.CreateUserAsync(context);
        var joinerId = await TestData.CreateUserAsync(context);

        var created = await Service(context).CreateAsync(hostId, CreateRequest(fixture.GameId));
        Assert.True(created.Succeeded);

        var sessionId = created.Value!.Id;

        Assert.True((await Service(context).StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest())).Succeeded);
        Assert.True((await Service(context).JoinAsync(joinerId, sessionId, new JoinMultiplayerSessionRequest { ProtocolVersion = 1 })).Succeeded);
        Assert.True((await Service(context).LeaveAsync(joinerId, sessionId, new LeaveMultiplayerSessionRequest())).Succeeded);
        Assert.True((await Service(context).CloseAsync(hostId, sessionId, new CloseMultiplayerSessionRequest())).Succeeded);
    }

    // -----------------------------------------------------------------------------------------
    // "where am I?"
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Listing_returns_only_the_callers_own_sessions_and_an_empty_list_otherwise()
    {
        var (hostId, sessionId, _) = await OpenSessionAsync();

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        var mine = await Service(context).ListForUserAsync(hostId, new MultiplayerSessionQuery());
        Assert.True(mine.Succeeded);
        Assert.Equal(sessionId, Assert.Single(mine.Value!).Id);

        // Not an error, and not somebody else's lobby list — just empty.
        var theirs = await Service(context).ListForUserAsync(strangerId, new MultiplayerSessionQuery());
        Assert.True(theirs.Succeeded);
        Assert.Empty(theirs.Value!);
    }
}
