using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Play;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// The session lifecycle. See <see cref="IMultiplayerSessionService"/> for the contract.
/// <para>
/// Three rules shape everything below.
/// </para>
/// <para>
/// **Capacity and membership are decided by the database, not by this class** — a conditional UPDATE
/// and filtered unique indexes, so the guarantees survive requests that arrive in the same
/// millisecond. That includes one account holding one live seat across *all* sessions, which is an
/// index (<c>UQ_SessionPlayer_OneLiveSeat</c>) rather than a check here.
/// </para>
/// <para>
/// **Every state move is a conditional UPDATE guarded on the state it expects**, with the legal
/// sources read from <see cref="MultiplayerSessionTransitions"/>. Not on the row version: a host
/// heartbeats four times a minute and every join touches the session row, so a move guarded on the
/// version it read loses to traffic that changed nothing it depends on. It used to — and a leave or a
/// close that lost that race answered 200 while doing nothing. A move guarded on state loses only to a
/// move that actually made it illegal.
/// </para>
/// <para>
/// **Every multi-statement transaction touches the session row first**, then its memberships. One
/// lock order everywhere is what keeps two leaves, a join and a close on the same session from
/// deadlocking each other; statements outside a transaction hold nothing and cannot join a cycle.
/// </para>
/// <para>
/// And throughout: **the caller is always the JWT subject**. No method reads an identity out of a
/// request body, so impersonation is not something checked for here — it cannot be expressed.
/// </para>
/// </summary>
public class MultiplayerSessionService : IMultiplayerSessionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerRequestLogStore _log;
    private readonly ISessionLessonMatcher _lessons;
    private readonly IPlaySelectionResolver _play;
    private readonly ILanguageService _languageService;
    private readonly IRosterNameResolver _names;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<MultiplayerSessionService> _logger;

    /// <summary>
    /// How many times a join re-attempts before giving up.
    /// <para>
    /// Retries exist for two genuinely transient losses: the capacity UPDATE finding the row moved
    /// underneath it, and two joiners choosing the same free seat. Both resolve within an attempt or
    /// two at any realistic session size, so a small bound is enough — and a bound is what stops a
    /// pathological loop under sustained contention.
    /// </para>
    /// </summary>
    private const int JoinAttempts = 4;

    /// <summary>
    /// The most roster entries a heartbeat is read for. A realtime room holds a handful of players;
    /// a client naming thousands is either broken or probing, and neither should cost a query per id.
    /// </summary>
    private const int MaxReportedPerHeartbeat = 64;

    public MultiplayerSessionService(
        ApplicationDbContext dbContext,
        MultiplayerRequestLogStore log,
        ISessionLessonMatcher lessons,
        IPlaySelectionResolver play,
        ILanguageService languageService,
        IRosterNameResolver names,
        IOptions<MultiplayerOptions> options,
        ILogger<MultiplayerSessionService> logger)
    {
        _dbContext = dbContext;
        _log = log;
        _lessons = lessons;
        _play = play;
        _languageService = languageService;
        _names = names;
        _options = options.Value;
        _logger = logger;
    }

    // ---- create --------------------------------------------------------------------------------

    public Task<ServiceResult<MultiplayerSessionDto>> CreateAsync(
        Guid userId,
        CreateMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(userId, request, MultiplayerOperations.Create, reserved: null, cancellationToken);

    /// <summary>
    /// What turns a create into a reserved room: who may sit in it, and — for a rematch — the match it
    /// follows. A live challenge is a reserved room with no match before it.
    /// </summary>
    internal sealed record ReservationPlan(
        IReadOnlyCollection<Guid> Roster,
        Guid? RematchOfSessionId = null,
        Guid? TournamentMatchId = null,
        int? ExactPlayers = null);

    /// <summary>
    /// A private room reserved for a named roster, hosted by the caller — the room behind a live
    /// challenge or a party's play. The ordinary create in every other respect, idempotent on the request id.
    /// </summary>
    internal Task<ServiceResult<MultiplayerSessionDto>> CreateReservedAsync(
        Guid userId,
        CreateMultiplayerSessionRequest request,
        IReadOnlyCollection<Guid> roster,
        string operation,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(userId, request, operation, new ReservationPlan(roster), cancellationToken);

    /// <summary>
    /// The room for one tournament pairing: reserved for the pair, exactly two seats — it cannot start
    /// until both are in — and at most one live per pairing. Hosted by the caller, who pressed play
    /// first. Runs inside the caller's transaction when there is one.
    /// </summary>
    internal Task<ServiceResult<MultiplayerSessionDto>> CreateTournamentRoomAsync(
        Guid userId,
        CreateMultiplayerSessionRequest request,
        IReadOnlyCollection<Guid> pair,
        Guid tournamentMatchId,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(
            userId,
            request,
            MultiplayerOperations.TournamentPlay,
            new ReservationPlan(pair, TournamentMatchId: tournamentMatchId, ExactPlayers: pair.Count),
            cancellationToken);

    /// <summary>The pairing's live room, if one of the pair has opened it.</summary>
    internal Task<Guid?> LiveTournamentRoomAsync(Guid tournamentMatchId, CancellationToken cancellationToken = default) =>
        _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.TournamentMatchId == tournamentMatchId
                        && s.State != MultiplayerSessionState.Closed
                        && s.State != MultiplayerSessionState.Abandoned
                        && s.State != MultiplayerSessionState.Failed)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Ends a room that never started, inside the caller's transaction, releasing every seat — the
    /// close a tournament makes when a pairing's deadline passes. A room that has started is left
    /// alone: its result decides the pairing. Returns whether this call ended it.
    /// </summary>
    internal async Task<bool> CloseUnstartedAsync(Guid sessionId, SessionClosedReason reason, CancellationToken cancellationToken = default)
    {
        // Guarded in the statement, not by a read first: a host pressing start in the same instant
        // either lands before this (and the room is left to its result) or after (and finds it closed).
        var closed = await CloseLockedAsync(sessionId, reason, DateTime.UtcNow, cancellationToken, onlyIfUnstarted: true);

        if (closed)
            MultiplayerMetrics.SessionsEnded.Add(1, MultiplayerMetrics.Tag("reason", reason.ToString()));

        return closed;
    }

    /// <summary>A session as its readers see it. For a tournament handing a pairing's room to the second of the pair.</summary>
    internal Task<MultiplayerSessionDto> DescribeAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        BuildAsync(sessionId, cancellationToken);

    /// <summary>
    /// A create, or — with <paramref name="reserved"/> — a reserved room (a rematch, a live challenge).
    /// One path, so a reserved room can never skip a check an ordinary create makes.
    /// </summary>
    private async Task<ServiceResult<MultiplayerSessionDto>> CreateCoreAsync(
        Guid userId,
        CreateMultiplayerSessionRequest request,
        string operation,
        ReservationPlan? reserved,
        CancellationToken cancellationToken)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, operation, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        if (!IsAcceptedProtocol(request.ProtocolVersion))
            return ProtocolMismatch<MultiplayerSessionDto>(request.ProtocolVersion);

        var transportName = (request.TransportSessionName ?? string.Empty).Trim();

        if (transportName.Length == 0)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.ValidationFailed,
                ServiceErrorKind.Validation,
                "transportSessionName is required.");

        var game = await _dbContext.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.GameId, cancellationToken);

        if (game is null)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.GameNotFound,
                ServiceErrorKind.NotFound,
                $"Game {request.GameId} does not exist.");

        if (!game.SupportsMultiplayer)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.GameNotMultiplayer,
                ServiceErrorKind.Conflict,
                $"Game {game.GameKey} is not flagged as supporting multiplayer.");

        // A fast refusal for the ordinary case. The guarantee itself is the one-live-seat index,
        // which catches the overlapping create this read cannot see.
        if (await HasActiveMembershipAsync(userId, cancellationToken))
            return AlreadyInSession<MultiplayerSessionDto>();

        // The catalog is authoritative for seat counts, and these are **copied** rather than read
        // through — editing the game row later must not resize a match that is already running.
        var catalogMax = Math.Max(1, game.MaxPlayers);
        var catalogMin = Math.Clamp(game.MinPlayers, 1, catalogMax);
        var maxPlayers = Math.Clamp(request.MaxPlayers ?? catalogMax, catalogMin, catalogMax);
        var minPlayers = catalogMin;

        // A tournament pairing's room seats exactly its pair, and cannot start without both.
        if (reserved?.ExactPlayers is { } exact)
        {
            if (exact > catalogMax)
                return ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.ValidationFailed,
                    ServiceErrorKind.Validation,
                    $"Game {game.GameKey} seats {catalogMax}; this room needs {exact}.");

            maxPlayers = exact;
            minPlayers = exact;
        }

        var visibility = reserved is not null || request.Visibility is SessionVisibility.Private
            ? SessionVisibility.Private
            : SessionVisibility.Public;

        // Which rules, and whether this account may play them with this many seats. The same gate a
        // solo run passes, run once here so a match cannot be formed in a mode nobody may enter.
        var selection = await _play.ResolveAsync(
            userId,
            new PlaySelectionRequest
            {
                GameId = game.Id,
                ModeKey = request.ModeKey,
                ContextKey = request.EventId is null ? null : PlayContextTokens.Event,
                EventId = request.EventId,
                PlayerCount = maxPlayers
            },
            cancellationToken);

        if (!selection.Succeeded)
            return new ServiceResult<MultiplayerSessionDto>
            {
                ErrorKind = selection.ErrorKind,
                Errors = selection.Errors,
                Error = selection.Error,
                Details = selection.Details
            };

        var play = selection.Value!;

        // The language the host is playing in. Questions exist per language, so it is part of what
        // makes two players able to share a lesson at all.
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var session = new MultiplayerSession
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            HostUserId = userId,
            TransportSessionName = transportName,
            TransportRegion = string.IsNullOrWhiteSpace(request.TransportRegion)
                ? null
                : request.TransportRegion.Trim(),
            JoinCode = visibility is SessionVisibility.Private ? GenerateJoinCode() : null,
            State = MultiplayerSessionState.Creating,
            Visibility = visibility,
            MaxPlayers = maxPlayers,
            MinPlayers = minPlayers,

            // The host occupies a seat from the instant the row exists.
            CurrentPlayerCount = 1,
            ProtocolVersion = request.ProtocolVersion,
            CurriculumPathJson = MultiplayerMappings.SerializePath(request.CurriculumPath),
            LessonId = request.CurriculumPath?.LessonId,

            // Subject-scoped only when no lesson was named: a client that still picks the exact
            // lesson keeps the behaviour it always had, down to the same candidate index.
            SubjectId = request.CurriculumPath?.LessonId is null ? request.CurriculumPath?.SubjectId : null,
            LangId = langId,
            ModeId = play.ModeId,
            EventId = play.EventId,
            IsRanked = request.IsRanked,
            RematchOfSessionId = reserved?.RematchOfSessionId,
            TournamentMatchId = reserved?.TournamentMatchId,
            IsReserved = reserved is not null,
            CreatedAtUtc = now,
            LastHeartbeatAtUtc = now
        };

        // The candidate set starts as everything the host themselves can play, and only ever narrows
        // from there. A host with nothing playable in the subject is refused here rather than left
        // hosting a room nobody can ever match into.
        if (session.SubjectId is { } subjectId)
        {
            var eligible = await _lessons.EligibleLessonsAsync(
                userId, game.Id, subjectId, langId, cancellationToken);

            if (eligible.Count == 0)
                return ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.PlayNoSharedLesson,
                    ServiceErrorKind.Conflict,
                    "You have no unlocked lessons with questions in this subject yet.");

            foreach (var lesson in eligible)
            {
                session.EligibleLessons.Add(new MultiplayerSessionEligibleLesson
                {
                    SessionId = session.Id,
                    LessonId = lesson.LessonId,
                    SummedBestPercent = lesson.BestPercent
                });
            }
        }

        _dbContext.MultiplayerSessions.Add(session);
        _dbContext.MultiplayerSessionPlayers.Add(new MultiplayerSessionPlayer
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Slot = 0,
            IsHost = true,
            Status = SessionPlayerStatus.Joined,
            JoinedAtUtc = now,
            LastSeenAtUtc = now
        });

        // In the same SaveChanges as the session: a reserved room must never exist, even for an
        // instant, without the list of who it is reserved for.
        foreach (var reservedFor in reserved?.Roster ?? [])
        {
            _dbContext.MultiplayerSessionReservations.Add(new MultiplayerSessionReservation
            {
                SessionId = session.Id,
                UserId = reservedFor,
                ReservedAtUtc = now
            });
        }

        try
        {
            // One SaveChanges, so one transaction: **a session can never exist without its host.**
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            Detach();

            // **A twin of this very request first.** A phone retrying a create whose first attempt
            // is still running sends the same key *and the same room name*, both miss the log (it is
            // written only on success), and the first to commit makes the other hit an index — the
            // room name's or the one-live-seat one, whichever the plan checks first. If the twin has
            // finished, its answer is this request's answer.
            if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, operation, cancellationToken) is { } twin)
                return ServiceResult<MultiplayerSessionDto>.Success(twin);

            // Another player of the same match asked for its rematch a moment earlier and won the
            // one-live-rematch index. Their room is this caller's answer; the room this caller named
            // is simply never brought up.
            if (reserved?.RematchOfSessionId is { } endedId && await LiveRematchOfAsync(endedId, cancellationToken) is { } first)
                return await ExistingRematchAsync(userId, requestId, first, cancellationToken);

            // The same for a tournament pairing: the other of the pair pressed play a moment earlier.
            if (reserved?.TournamentMatchId is { } pairing && await LiveTournamentRoomAsync(pairing, cancellationToken) is { } opened)
                return await ExistingReservedRoomAsync(userId, requestId, operation, opened, cancellationToken);

            // Which index bit is worked out by re-reading rather than by parsing the SQL error
            // text — the message format is not a contract, and the answer is cheap to look up.
            if (await TransportNameIsTakenAsync(transportName, cancellationToken))
                return Transition(operation, ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.TransportNameTaken,
                    ServiceErrorKind.Conflict,
                    $"Transport session name '{transportName}' is already in use by a live session."));

            // The one-live-seat index: this account became seated somewhere else while this create
            // was in flight.
            if (await HasActiveMembershipAsync(userId, cancellationToken))
                return Transition(operation, AlreadyInSession<MultiplayerSessionDto>());

            // Only the join code is left, and it is server-minted — a collision is ours to absorb,
            // not the caller's to see.
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.TransportNameTaken,
                ServiceErrorKind.Conflict,
                "Session could not be created because of a key collision. Retry with a new name.");
        }

        var dto = await BuildAsync(session.Id, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, operation, session.Id, dto, StatusCodes.Created, cancellationToken);

        MultiplayerMetrics.SessionsCreated.Add(1, MultiplayerMetrics.Tag("visibility", visibility.ToString()));
        Transition(operation, ServiceResult<MultiplayerSessionDto>.Success(dto));
        _logger.LogInformation(
            "Multiplayer session {SessionId} created by {UserId} for game {GameId} ({Visibility}, {MaxPlayers} seats).",
            session.Id, userId, game.Id, visibility, maxPlayers);

        return ServiceResult<MultiplayerSessionDto>.Success(dto);
    }

    // ---- start ---------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> StartAsync(
        Guid userId,
        Guid sessionId,
        StartMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.Start, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await ReadSessionAsync(sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (session.HostUserId != userId)
            return Transition(MultiplayerOperations.Start, NotSessionHost<MultiplayerSessionDto>());

        var from = session.State;

        // Which move "I am ready" means depends on where the session actually is:
        //  - Creating → Created, the confirmation half of the create saga: the transport room came
        //    up, so the session becomes joinable. Until this lands nobody can get in, which is what
        //    stops players being seated into a room that never existed.
        //  - Created → Running, the commit-to-start. Joins close and the clock starts. `Starting` is
        //    a real state and a legal transition, but one call carries the session all the way to
        //    Running — there is no second request to wait for, so lingering in Starting would only
        //    open a window in which nothing can happen.
        MultiplayerSessionState to;

        switch (from)
        {
            case MultiplayerSessionState.Creating:
                to = MultiplayerSessionState.Created;
                break;

            case MultiplayerSessionState.Created:
                if (session.CurrentPlayerCount < session.MinPlayers)
                    return Transition(MultiplayerOperations.Start, BelowMinPlayers(session.CurrentPlayerCount, session.MinPlayers));

                to = MultiplayerSessionState.Running;
                break;

            default:
                return Transition(MultiplayerOperations.Start, InvalidTransition<MultiplayerSessionDto>(from));
        }

        // A subject-scoped match that is starting without its lesson stamped — started by its host
        // alone, where the game allows that — gets one now. **A match must never run without the
        // lesson it plays**: the client loads its questions from exactly this.
        if (to == MultiplayerSessionState.Running && session.SubjectId is not null && session.LessonId is null)
            await _lessons.EnsureLessonAsync(sessionId, userId, cancellationToken);

        var now = DateTime.UtcNow;

        // **The whole guard is the WHERE clause.** State, host and seat count are all re-checked by
        // the statement that makes the move, so nothing that changed since the read above can be
        // overwritten — and nothing that changed without mattering (a heartbeat, a join) can refuse it.
        var candidate = _dbContext.MultiplayerSessions
            .Where(s => s.Id == sessionId && s.HostUserId == userId && s.State == from);

        var moved = to == MultiplayerSessionState.Running
            ? await candidate
                .Where(s => s.CurrentPlayerCount >= s.MinPlayers)
                .ExecuteUpdateAsync(set => set
                        .SetProperty(s => s.State, to)
                        .SetProperty(s => s.StartedAtUtc, now)
                        .SetProperty(s => s.LastHeartbeatAtUtc, now),
                    cancellationToken)
            : await candidate
                .ExecuteUpdateAsync(set => set
                        .SetProperty(s => s.State, to)
                        .SetProperty(s => s.LastHeartbeatAtUtc, now),
                    cancellationToken);

        if (moved == 0)
        {
            // Something that genuinely matters changed. Re-read and say which.
            var current = await ReadSessionAsync(sessionId, cancellationToken);

            if (current is null)
                return SessionNotFound<MultiplayerSessionDto>(sessionId);

            if (current.HostUserId != userId)
                return Transition(MultiplayerOperations.Start, NotSessionHost<MultiplayerSessionDto>());

            if (current.State == from && to == MultiplayerSessionState.Running)
                return Transition(MultiplayerOperations.Start, BelowMinPlayers(current.CurrentPlayerCount, current.MinPlayers));

            return Transition(MultiplayerOperations.Start, InvalidTransition<MultiplayerSessionDto>(current.State));
        }

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Start, sessionId, dto, StatusCodes.Ok, cancellationToken);

        _logger.LogInformation(
            "Multiplayer session {SessionId} moved {From} -> {To} by host {UserId}.", sessionId, from, to, userId);

        return Transition(MultiplayerOperations.Start, ServiceResult<MultiplayerSessionDto>.Success(dto));
    }

    // ---- join ----------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> JoinAsync(
        Guid userId,
        Guid sessionId,
        JoinMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.Join, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        // A private session behind its code, once the rollout switch is on — see
        // MultiplayerOptions.RequireJoinCodeForPrivateSessions. Refused exactly as a session that does
        // not exist, so the id route cannot be used to learn which ids are private rooms. Anyone who
        // has held a seat here keeps the id route: that is a player reconnecting, not a stranger. So
        // does a player a rematch is reserved for — the rematch answer handed them this id to join.
        if (_options.RequireJoinCodeForPrivateSessions
            && await _dbContext.MultiplayerSessions.AnyAsync(
                s => s.Id == sessionId && s.Visibility == SessionVisibility.Private, cancellationToken)
            && !await IsMemberAsync(userId, sessionId, cancellationToken)
            && !await IsReservedForAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        return await SeatAndRecordAsync(userId, sessionId, request.ProtocolVersion, requestId, MultiplayerOperations.Join, cancellationToken);
    }

    public async Task<ServiceResult<MultiplayerSessionDto>> JoinByCodeAsync(
        Guid userId,
        JoinMultiplayerSessionByCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.JoinByCode, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var code = NormaliseJoinCode(request.JoinCode);

        // Only live private sessions hold a code — the unique index releases it the moment a session
        // ends — so a code that resolves is a room that can still be entered.
        var sessionId = code is null
            ? null
            : await _dbContext.MultiplayerSessions
                .AsNoTracking()
                .Where(s => s.JoinCode == code
                            && s.Visibility == SessionVisibility.Private
                            && s.State != MultiplayerSessionState.Closed
                            && s.State != MultiplayerSessionState.Failed
                            && s.State != MultiplayerSessionState.Abandoned)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (sessionId is not { } found)
        {
            MultiplayerMetrics.Seats.Add(1, MultiplayerMetrics.Tag("outcome", "JOIN_CODE_UNKNOWN"));

            // One answer for unknown, ended and malformed, and no echo of the code: see the interface.
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionNotFound,
                ServiceErrorKind.NotFound,
                "No live session holds that join code.");
        }

        return await SeatAndRecordAsync(userId, found, request.ProtocolVersion, requestId, MultiplayerOperations.JoinByCode, cancellationToken);
    }

    /// <summary>
    /// A join code as typed, in the stored form: upper case, with the spaces and hyphens people add
    /// when reading six characters aloud removed. Null when what is left cannot be a code at all.
    /// </summary>
    private static string? NormaliseJoinCode(string? typed)
    {
        var code = new string((typed ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());

        return code.Length is > 0 and <= 8 ? code : null;
    }

    /// <summary>The seat, its idempotency record, and the overlapping-retry rule, shared by both join routes.</summary>
    private async Task<ServiceResult<MultiplayerSessionDto>> SeatAndRecordAsync(
        Guid userId,
        Guid sessionId,
        int protocolVersion,
        string requestId,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await SeatAsync(userId, sessionId, protocolVersion, cancellationToken);

        if (result.Succeeded && result.Value is { } seated)
            await _log.RecordAsync(userId, requestId, operation, sessionId, seated, StatusCodes.Ok, cancellationToken);
        else if (result.Error?.Code == ApiErrors.AlreadyInSession.Code)
            return await ReplayOrAsync(userId, requestId, operation, result, cancellationToken);

        return result;
    }

    /// <summary>
    /// Seats a caller in a session. Split out from <see cref="JoinAsync"/> because matchmaking runs
    /// exactly this loop against each candidate in turn — a second implementation there is how the
    /// two paths would come to disagree about capacity.
    /// </summary>
    internal async Task<ServiceResult<MultiplayerSessionDto>> SeatAsync(
        Guid userId,
        Guid sessionId,
        int protocolVersion,
        CancellationToken cancellationToken = default)
    {
        var result = await SeatCoreAsync(userId, sessionId, protocolVersion, cancellationToken);

        MultiplayerMetrics.Seats.Add(1, MultiplayerMetrics.Tag("outcome", MultiplayerMetrics.Outcome(result)));

        return result;
    }

    private async Task<ServiceResult<MultiplayerSessionDto>> SeatCoreAsync(
        Guid userId,
        Guid sessionId,
        int protocolVersion,
        CancellationToken cancellationToken)
    {
        if (!IsAcceptedProtocol(protocolVersion))
            return ProtocolMismatch<MultiplayerSessionDto>(protocolVersion);

        // One account plays one match at a time. This read is the fast, friendly refusal; the
        // **guarantee** is UQ_SessionPlayer_OneLiveSeat, which the insert below runs into when two
        // joins for the same account overlap across two different sessions — the case this read,
        // by itself, used to let through.
        if (await HasActiveMembershipAsync(userId, cancellationToken))
            return AlreadyInSession<MultiplayerSessionDto>();

        for (var attempt = 0; attempt < JoinAttempts; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            // **The whole capacity guarantee is this one statement.** Two clients racing for the
            // last seat both run it; exactly one finds CurrentPlayerCount < MaxPlayers still true
            // and the other affects zero rows. No SELECT takes part in the decision, so READ
            // COMMITTED is sufficient — and it is the first thing the transaction locks, which is
            // the session-first order every other transaction here follows.
            //
            // The host's removals are checked here too, in the same statement, rather than by a read
            // beforehand: a removal holds the session row while it writes its ban, so this UPDATE
            // waits for it and then sees it. A read would let a rejoin slip in between. A reserved
            // session's allow-list is checked the same way, for the same reason.
            var rows = await _dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE [MultiplayerSessions]
                SET [CurrentPlayerCount] = [CurrentPlayerCount] + 1
                WHERE [Id] = {0}
                  AND [State] = {1}
                  AND [CurrentPlayerCount] < [MaxPlayers]
                  AND [ProtocolVersion] = {2}
                  AND NOT EXISTS (SELECT 1 FROM [MultiplayerSessionBans] AS [b]
                                  WHERE [b].[SessionId] = {0} AND [b].[UserId] = {3})
                  AND ([IsReserved] = 0
                       OR EXISTS (SELECT 1 FROM [MultiplayerSessionReservations] AS [r]
                                  WHERE [r].[SessionId] = {0} AND [r].[UserId] = {3}))
                """,
                [sessionId, WireEnum.ToWire(MultiplayerSessionState.Created), protocolVersion, userId],
                cancellationToken);

            if (rows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                var refusal = await ClassifyFailedSeatAsync(sessionId, userId, protocolVersion, cancellationToken);

                // Null means nothing was actually wrong — the row moved between the UPDATE and the
                // re-read. Try again rather than inventing a reason.
                if (refusal is not null)
                    return refusal;

                continue;
            }

            var taken = await _dbContext.MultiplayerSessionPlayers
                .AsNoTracking()
                .Where(p => p.SessionId == sessionId
                            && p.Status != SessionPlayerStatus.Left
                            && p.Status != SessionPlayerStatus.Removed)
                .Select(p => p.Slot)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;

            _dbContext.MultiplayerSessionPlayers.Add(new MultiplayerSessionPlayer
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = userId,
                Slot = FirstFreeSlot(taken),
                IsHost = false,
                Status = SessionPlayerStatus.Joined,
                JoinedAtUtc = now,
                LastSeenAtUtc = now
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);

                // What this player can still play, intersected with what everyone already seated
                // can. Inside the seat's own transaction: a player who turns out to share no lesson
                // must not be left holding a seat in a match that cannot start, and matchmaking
                // reads the refusal as "try the next session".
                var narrowed = await _lessons.NarrowForSeatAsync(sessionId, userId, cancellationToken);

                if (!narrowed.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    Detach();

                    return new ServiceResult<MultiplayerSessionDto>
                    {
                        ErrorKind = narrowed.ErrorKind,
                        Errors = narrowed.Errors,
                        Error = narrowed.Error,
                        Details = narrowed.Details
                    };
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // Rolling back is what makes the increment above safe: the seat is released with
                // the same transaction that took it, so a failed insert can never leave a session
                // reporting a player it does not have.
                await transaction.RollbackAsync(cancellationToken);
                Detach();

                // Seated elsewhere — in this session (UQ_SessionPlayer_Active) or another one
                // (UQ_SessionPlayer_OneLiveSeat) — by a request that overlapped this one.
                if (await HasActiveMembershipAsync(userId, cancellationToken))
                    return AlreadyInSession<MultiplayerSessionDto>();

                // Otherwise two joiners picked the same free seat. Re-read and take another.
                continue;
            }

            _logger.LogInformation("Player {UserId} seated in multiplayer session {SessionId}.", userId, sessionId);

            return ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken));
        }

        // Every attempt lost a race. Reporting the session as full is the honest answer — under this
        // much contention it effectively is.
        return ServiceResult<MultiplayerSessionDto>.Failure(
            ApiErrors.SessionFull,
            ServiceErrorKind.Conflict,
            $"Could not seat a player in session {sessionId} after {JoinAttempts} attempts.");
    }

    /// <summary>
    /// Works out why the capacity UPDATE matched nothing. Returns null when the session looks
    /// perfectly joinable, which means the loss was transient and the caller should retry.
    /// </summary>
    private async Task<ServiceResult<MultiplayerSessionDto>?> ClassifyFailedSeatAsync(
        Guid sessionId,
        Guid userId,
        int protocolVersion,
        CancellationToken cancellationToken)
    {
        var session = await ReadSessionAsync(sessionId, cancellationToken);

        if (session is null)
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        // Told plainly, and only ever to the account that was removed: a child asking "why can't I
        // get back in" deserves the real answer rather than a room that seems to have vanished.
        if (await IsRemovedAsync(userId, sessionId, cancellationToken))
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionRemoved,
                ServiceErrorKind.Forbidden,
                $"The host removed this account from session {sessionId}.");

        if (session.IsReserved && !await IsReservedForAsync(userId, sessionId, cancellationToken))
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionReserved,
                ServiceErrorKind.Forbidden,
                $"Session {sessionId} is reserved for the players of the match before it.");

        if (session.ProtocolVersion != protocolVersion)
            return ProtocolMismatch<MultiplayerSessionDto>(protocolVersion);

        if (session.State != MultiplayerSessionState.Created)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionClosed,
                ServiceErrorKind.Conflict,
                $"Session {sessionId} is {session.State} and is not accepting players.",
                // **The state belongs in details.** One code covers every unjoinable state, so
                // without this a caller cannot tell "the match ended" from "the host never
                // confirmed the transport room and it was swept" — which are the same refusal and
                // completely different problems. Additive: no client mapping changes for it.
                new Dictionary<string, object?>
                {
                    ["state"] = WireEnum.ToWire(session.State),
                    ["closedReason"] = session.ClosedReason is { } reason ? WireEnum.ToWire(reason) : null
                });

        if (session.CurrentPlayerCount >= session.MaxPlayers)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionFull,
                ServiceErrorKind.Conflict,
                $"Session {sessionId} has all {session.MaxPlayers} seats taken.",
                new Dictionary<string, object?>
                {
                    ["currentPlayerCount"] = session.CurrentPlayerCount,
                    ["maxPlayers"] = session.MaxPlayers
                });

        return null;
    }

    // ---- leave ---------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> LeaveAsync(
        Guid userId,
        Guid sessionId,
        LeaveMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.Leave, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await ReadSessionAsync(sessionId, cancellationToken);

        // A removed player is already out, and — like every other read — is not shown the room again.
        if (session is null || await IsRemovedAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        // **The seat held now, not any seat ever held.** Someone who left and rejoined has two rows
        // here, and an unordered "the caller's membership" could return the departed one — which
        // answered "already gone" and left the live seat exactly where it was.
        var memberships = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId && p.UserId == userId)
            .Select(p => new { p.Id, p.Status })
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        var seat = memberships.FirstOrDefault(p => !p.Status.HasDeparted());

        // **Idempotent.** Already gone, or the session already ended — either way the caller's
        // intent holds and there is nothing to undo. A second leave is not an error; it is a retry.
        if (seat is null || session.State.IsTerminal())
            return Transition(MultiplayerOperations.Leave,
                ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken)));

        var now = DateTime.UtcNow;
        var endedEmpty = false;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // Session first — see the class summary. Held to commit, so the roster read below and
            // the count written from it cannot be disturbed by a join or another leave.
            var locked = await LockSessionAsync(sessionId, cancellationToken);

            if (locked is null || locked.State.IsTerminal())
            {
                await transaction.RollbackAsync(cancellationToken);
                return Transition(MultiplayerOperations.Leave,
                    ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken)));
            }

            var released = await _dbContext.MultiplayerSessionPlayers
                .Where(p => p.Id == seat.Id
                            && p.Status != SessionPlayerStatus.Left
                            && p.Status != SessionPlayerStatus.Removed)
                .ExecuteUpdateAsync(set => set
                        .SetProperty(p => p.Status, SessionPlayerStatus.Left)
                        .SetProperty(p => p.LeftAtUtc, now)
                        .SetProperty(p => p.IsHost, false),
                    cancellationToken);

            if (released == 0)
            {
                // The sweeper released this seat in the gap. The caller is out either way.
                await transaction.RollbackAsync(cancellationToken);
                return Transition(MultiplayerOperations.Leave,
                    ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken)));
            }

            var remaining = await _dbContext.MultiplayerSessionPlayers
                .AsNoTracking()
                .Where(p => p.SessionId == sessionId
                            && p.Status != SessionPlayerStatus.Left
                            && p.Status != SessionPlayerStatus.Removed)
                .OrderBy(p => p.Slot)
                .Select(p => new { p.UserId, p.Status })
                .ToListAsync(cancellationToken);

            if (remaining.Count == 0)
            {
                // Nobody left to play. Closing it here rather than waiting for the sweeper means the
                // transport name is released immediately, so the same host can start again at once.
                endedEmpty = await CloseLockedAsync(sessionId, SessionClosedReason.Empty, now, cancellationToken);
            }
            else if (locked.HostUserId == userId)
            {
                // Authority passes to the lowest seat still connected, else the lowest seat —
                // deterministic, so every client can predict the same successor rather than waiting
                // to be told.
                var successor = (remaining.FirstOrDefault(p => p.Status == SessionPlayerStatus.Connected)
                                 ?? remaining[0]).UserId;

                await _dbContext.MultiplayerSessions
                    .Where(s => s.Id == sessionId)
                    .ExecuteUpdateAsync(set => set
                            .SetProperty(s => s.CurrentPlayerCount, remaining.Count)
                            .SetProperty(s => s.HostUserId, successor),
                        cancellationToken);

                await SetHostFlagsAsync(sessionId, successor, cancellationToken);
            }
            else
            {
                await _dbContext.MultiplayerSessions
                    .Where(s => s.Id == sessionId)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.CurrentPlayerCount, remaining.Count), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (endedEmpty)
            MultiplayerMetrics.SessionsEnded.Add(1, MultiplayerMetrics.Tag("reason", SessionClosedReason.Empty.ToString()));

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Leave, sessionId, dto, StatusCodes.Ok, cancellationToken);

        _logger.LogInformation("Player {UserId} left multiplayer session {SessionId}.", userId, sessionId);

        return Transition(MultiplayerOperations.Leave, ServiceResult<MultiplayerSessionDto>.Success(dto));
    }

    // ---- remove --------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> RemovePlayerAsync(
        Guid userId,
        Guid sessionId,
        RemovePlayerRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.Remove, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await ReadSessionAsync(sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (session.HostUserId != userId)
            return Transition(MultiplayerOperations.Remove, NotSessionHost<MultiplayerSessionDto>());

        var target = request.UserId;

        if (target == userId)
            return Transition(MultiplayerOperations.Remove, ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.ValidationFailed,
                ServiceErrorKind.Validation,
                "The host cannot remove themselves. Leave the session instead."));

        // Anyone who has ever held a seat here — so removing somebody who just left still bans them.
        // A stranger's id is refused exactly as host transfer refuses one.
        if (!await _dbContext.MultiplayerSessionPlayers.AnyAsync(
                p => p.SessionId == sessionId && p.UserId == target, cancellationToken))
            return Transition(MultiplayerOperations.Remove, ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.NotSessionMember,
                ServiceErrorKind.Forbidden,
                $"User {target} has never held a seat in session {sessionId}."));

        var now = DateTime.UtcNow;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // Session first, and held: a rejoin's capacity UPDATE queues behind this lock and then sees
            // the ban below, which is what makes a removal stick under a racing rejoin.
            var locked = await LockSessionAsync(sessionId, cancellationToken);

            var refusal = locked switch
            {
                null => SessionNotFound<MultiplayerSessionDto>(sessionId),
                _ when locked.HostUserId != userId => NotSessionHost<MultiplayerSessionDto>(),
                _ when locked.State.IsTerminal() => ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.SessionClosed, ServiceErrorKind.Conflict, $"Session {sessionId} has already ended."),

                // Before the match only — see the interface for why a running match is off limits.
                _ when locked.State is not (MultiplayerSessionState.Creating or MultiplayerSessionState.Created) =>
                    InvalidTransition<MultiplayerSessionDto>(locked.State),
                _ => null
            };

            if (refusal is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Transition(MultiplayerOperations.Remove, refusal);
            }

            // Idempotent at the key: removing twice is the same removal.
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM [MultiplayerSessionBans] WHERE [SessionId] = {0} AND [UserId] = {1})
                    INSERT INTO [MultiplayerSessionBans] ([SessionId], [UserId], [BannedByUserId], [BannedAtUtc])
                    VALUES ({0}, {1}, {2}, {3});
                """,
                [sessionId, target, userId, now],
                cancellationToken);

            var released = await _dbContext.MultiplayerSessionPlayers
                .Where(p => p.SessionId == sessionId
                            && p.UserId == target
                            && p.Status != SessionPlayerStatus.Left
                            && p.Status != SessionPlayerStatus.Removed)
                .ExecuteUpdateAsync(set => set
                        .SetProperty(p => p.Status, SessionPlayerStatus.Removed)
                        .SetProperty(p => p.LeftAtUtc, now)
                        .SetProperty(p => p.IsHost, false),
                    cancellationToken);

            if (released > 0)
                await SeatCounts.RecountAsync(_dbContext, sessionId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Remove, sessionId, dto, StatusCodes.Ok, cancellationToken);

        _logger.LogInformation(
            "Player {TargetUserId} removed from multiplayer session {SessionId} by host {UserId}.",
            target, sessionId, userId);

        return Transition(MultiplayerOperations.Remove, ServiceResult<MultiplayerSessionDto>.Success(dto));
    }

    // ---- matches formed by the matchmaking worker ------------------------------------------------

    /// <summary>A match the matchmaking worker formed: who is in it (the host first) and on what terms.</summary>
    internal sealed record FormedMatch(
        IReadOnlyList<Guid> Players,
        Guid GameId,
        Guid ModeId,
        Guid? EventId,
        bool Rated,
        bool Public,
        int MaxPlayers,
        int ProtocolVersion,
        Guid LangId,
        Guid? SubjectId,
        Guid? LessonId,
        string TransportSessionName,
        string? TransportRegion);

    /// <summary>
    /// Creates the session for a formed match **inside the caller's transaction**, with every player
    /// already seated, so nobody can be beaten to a seat they were matched into.
    /// <para>
    /// The host's play gate runs as for any create. A subject-scoped match narrows its lessons through
    /// every seat, exactly as joins do; a player who no longer shares a lesson refuses the whole
    /// match, and the caller rolls it back. A player seated elsewhere in the meantime surfaces as the
    /// one-live-seat index's violation, also for the caller to handle — the indexes decide, as always.
    /// </para>
    /// </summary>
    internal async Task<ServiceResult<MultiplayerSession>> CreateFormedAsync(FormedMatch match, CancellationToken cancellationToken = default)
    {
        var host = match.Players[0];

        var game = await _dbContext.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == match.GameId, cancellationToken);

        if (game is null || !game.SupportsMultiplayer)
            return ServiceResult<MultiplayerSession>.Failure(
                ApiErrors.GameNotMultiplayer, ServiceErrorKind.Conflict, $"Game {match.GameId} cannot host a match.");

        var modeKey = await _dbContext.GameModes.AsNoTracking()
            .Where(m => m.Id == match.ModeId)
            .Select(m => m.ModeKey)
            .FirstOrDefaultAsync(cancellationToken);

        var selection = await _play.ResolveAsync(
            host,
            new PlaySelectionRequest
            {
                GameId = game.Id,
                ModeKey = modeKey,
                ContextKey = match.EventId is null ? null : PlayContextTokens.Event,
                EventId = match.EventId,
                PlayerCount = Math.Max(2, match.Players.Count)
            },
            cancellationToken);

        if (!selection.Succeeded)
            return new ServiceResult<MultiplayerSession>
            {
                ErrorKind = selection.ErrorKind,
                Errors = selection.Errors,
                Error = selection.Error,
                Details = selection.Details
            };

        var catalogMax = Math.Max(1, game.MaxPlayers);
        var catalogMin = Math.Clamp(game.MinPlayers, 1, catalogMax);

        if (match.Players.Count > catalogMax)
            return ServiceResult<MultiplayerSession>.Failure(
                ApiErrors.ValidationFailed, ServiceErrorKind.Validation, $"Game {game.GameKey} seats {catalogMax}.");

        var maxPlayers = Math.Clamp(match.MaxPlayers, Math.Max(catalogMin, match.Players.Count), catalogMax);
        var now = DateTime.UtcNow;

        var path = match.LessonId is { } lessonId ? new CurriculumPathDto { LessonId = lessonId }
            : match.SubjectId is { } pathSubject ? new CurriculumPathDto { SubjectId = pathSubject }
            : null;

        var session = new MultiplayerSession
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            HostUserId = host,
            TransportSessionName = match.TransportSessionName,
            TransportRegion = string.IsNullOrWhiteSpace(match.TransportRegion) ? null : match.TransportRegion.Trim(),
            JoinCode = match.Public ? null : GenerateJoinCode(),
            State = MultiplayerSessionState.Creating,
            Visibility = match.Public ? SessionVisibility.Public : SessionVisibility.Private,
            MaxPlayers = maxPlayers,
            MinPlayers = catalogMin,
            CurrentPlayerCount = match.Players.Count,
            ProtocolVersion = match.ProtocolVersion,
            CurriculumPathJson = MultiplayerMappings.SerializePath(path),
            LessonId = match.LessonId,
            SubjectId = match.LessonId is null ? match.SubjectId : null,
            LangId = match.LangId,
            ModeId = selection.Value!.ModeId,
            EventId = selection.Value.EventId,

            // Rated matches carry both flags; the client-facing one so a rated room reads as ranked.
            IsRanked = match.Rated,
            IsRated = match.Rated,
            CreatedAtUtc = now,
            LastHeartbeatAtUtc = now
        };

        if (session.SubjectId is { } subjectId)
        {
            var eligible = await _lessons.EligibleLessonsAsync(host, game.Id, subjectId, match.LangId, cancellationToken);

            if (eligible.Count == 0)
                return ServiceResult<MultiplayerSession>.Failure(
                    ApiErrors.PlayNoSharedLesson, ServiceErrorKind.Conflict, "The host has no lesson to play in this subject.");

            foreach (var lesson in eligible)
                session.EligibleLessons.Add(new MultiplayerSessionEligibleLesson
                {
                    SessionId = session.Id,
                    LessonId = lesson.LessonId,
                    SummedBestPercent = lesson.BestPercent
                });
        }

        _dbContext.MultiplayerSessions.Add(session);

        for (var slot = 0; slot < match.Players.Count; slot++)
        {
            _dbContext.MultiplayerSessionPlayers.Add(new MultiplayerSessionPlayer
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                UserId = match.Players[slot],
                Slot = slot,
                IsHost = slot == 0,
                Status = SessionPlayerStatus.Joined,
                JoinedAtUtc = now,
                LastSeenAtUtc = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var player in match.Players.Skip(1))
        {
            var narrowed = await _lessons.NarrowForSeatAsync(session.Id, player, cancellationToken);

            if (!narrowed.Succeeded)
                return new ServiceResult<MultiplayerSession>
                {
                    ErrorKind = narrowed.ErrorKind,
                    Errors = narrowed.Errors,
                    Error = narrowed.Error,
                    Details = narrowed.Details
                };
        }

        MultiplayerMetrics.SessionsCreated.Add(1, MultiplayerMetrics.Tag("visibility", session.Visibility.ToString()));

        return ServiceResult<MultiplayerSession>.Success(session);
    }

    /// <summary>
    /// Seats a whole group in an open room **inside the caller's transaction**: one statement takes
    /// every seat or none, so a party is never split across rooms or left half in. Refused (false) when
    /// the room has no space for all of them, has started, is reserved, or removed any of them.
    /// </summary>
    internal async Task<bool> SeatGroupAsync(
        Guid sessionId, IReadOnlyList<Guid> players, int protocolVersion, CancellationToken cancellationToken = default)
    {
        var placeholders = string.Join(", ", players.Select((_, i) => $"{{{i + 4}}}"));

        var rows = await _dbContext.Database.ExecuteSqlRawAsync(
            $$"""
            UPDATE [MultiplayerSessions]
            SET [CurrentPlayerCount] = [CurrentPlayerCount] + {1}
            WHERE [Id] = {0}
              AND [State] = {2}
              AND [CurrentPlayerCount] + {1} <= [MaxPlayers]
              AND [ProtocolVersion] = {3}
              AND [IsReserved] = 0
              AND NOT EXISTS (SELECT 1 FROM [MultiplayerSessionBans] AS [b]
                              WHERE [b].[SessionId] = {0} AND [b].[UserId] IN ({{placeholders}}))
            """,
            [sessionId, players.Count, WireEnum.ToWire(MultiplayerSessionState.Created), protocolVersion, .. players.Cast<object>()],
            cancellationToken);

        if (rows == 0)
            return false;

        var taken = await _dbContext.MultiplayerSessionPlayers.AsNoTracking()
            .Where(p => p.SessionId == sessionId && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed)
            .Select(p => p.Slot)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var player in players)
        {
            var slot = FirstFreeSlot(taken);
            taken.Add(slot);

            _dbContext.MultiplayerSessionPlayers.Add(new MultiplayerSessionPlayer
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                UserId = player,
                Slot = slot,
                IsHost = false,
                Status = SessionPlayerStatus.Joined,
                JoinedAtUtc = now,
                LastSeenAtUtc = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var player in players)
        {
            if (!(await _lessons.NarrowForSeatAsync(sessionId, player, cancellationToken)).Succeeded)
                return false;
        }

        return true;
    }

    // ---- rematch -------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> RematchAsync(
        Guid userId,
        Guid sessionId,
        RematchRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.Rematch, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var ended = await ReadSessionAsync(sessionId, cancellationToken);

        if (ended is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        // A rematch follows a match: one that was played, and is over. A lobby that never started has
        // nothing to rematch, and a match still running has seats its players still hold.
        if (ended.StartedAtUtc is not { } startedAt || !ended.State.IsTerminal())
            return Transition(MultiplayerOperations.Rematch, InvalidTransition<MultiplayerSessionDto>(ended.State));

        var roster = await RosterAtStartAsync(sessionId, startedAt, cancellationToken);

        // Someone who held a seat only before kick-off, or was removed, did not play this match and
        // does not get to reopen it with its players.
        if (!roster.Contains(userId))
            return Transition(MultiplayerOperations.Rematch, ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.NotSessionMember,
                ServiceErrorKind.Forbidden,
                $"Only a player of the match in session {sessionId} can ask for its rematch."));

        if (await LiveRematchOfAsync(sessionId, cancellationToken) is { } existing)
            return await ExistingRematchAsync(userId, requestId, existing, cancellationToken);

        // The ended match's own terms. Its curriculum path is the one its host sent, not the lesson
        // it ended up on: a subject match rematches on the subject, so the players meet a lesson
        // again rather than the one they have just answered; an exact-lesson match keeps its lesson.
        var modeKey = ended.ModeId is { } modeId
            ? await _dbContext.GameModes.AsNoTracking().Where(m => m.Id == modeId).Select(m => m.ModeKey).FirstOrDefaultAsync(cancellationToken)
            : null;

        var create = new CreateMultiplayerSessionRequest
        {
            GameId = ended.GameId,
            TransportSessionName = request.TransportSessionName,
            TransportRegion = request.TransportRegion ?? ended.TransportRegion,
            Visibility = SessionVisibility.Private,
            MaxPlayers = ended.MaxPlayers,

            // Never ranked: the same opponents replaying each other on demand is exactly the loop
            // ranked play must not be farmed through.
            IsRanked = false,
            ProtocolVersion = request.ProtocolVersion,
            CurriculumPath = MultiplayerMappings.DeserializePath(ended.CurriculumPathJson),
            ModeKey = modeKey,
            EventId = ended.EventId,
            RequestId = request.RequestId
        };

        return await CreateCoreAsync(
            userId, create, MultiplayerOperations.Rematch, new ReservationPlan(roster, sessionId), cancellationToken);
    }

    /// <summary>
    /// Who played the match: seated when it started — joined by then, not gone before it — and not
    /// removed. The same roster the match's result is decided over.
    /// </summary>
    private async Task<HashSet<Guid>> RosterAtStartAsync(Guid sessionId, DateTime startedAt, CancellationToken cancellationToken) =>
        (await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Removed
                        && p.JoinedAtUtc <= startedAt
                        && (p.LeftAtUtc == null || p.LeftAtUtc >= startedAt)
                        && !_dbContext.MultiplayerSessionBans.Any(b => b.SessionId == sessionId && b.UserId == p.UserId))
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync(cancellationToken))
        .ToHashSet();

    private Task<Guid?> LiveRematchOfAsync(Guid endedSessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.RematchOfSessionId == endedSessionId
                        && s.State != MultiplayerSessionState.Closed
                        && s.State != MultiplayerSessionState.Abandoned
                        && s.State != MultiplayerSessionState.Failed)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The rematch another player already opened, as this caller's answer. They are not seated: the
    /// room may not be up yet, and joining is the ordinary join once it is <c>Created</c>.
    /// </summary>
    private Task<ServiceResult<MultiplayerSessionDto>> ExistingRematchAsync(
        Guid userId,
        string requestId,
        Guid rematchId,
        CancellationToken cancellationToken) =>
        ExistingReservedRoomAsync(userId, requestId, MultiplayerOperations.Rematch, rematchId, cancellationToken);

    /// <summary>A reserved room someone else already opened, as this caller's answer — not seated; they join it once it is up.</summary>
    private async Task<ServiceResult<MultiplayerSessionDto>> ExistingReservedRoomAsync(
        Guid userId,
        string requestId,
        string operation,
        Guid roomId,
        CancellationToken cancellationToken)
    {
        var dto = await BuildAsync(roomId, cancellationToken);

        await _log.RecordAsync(userId, requestId, operation, roomId, dto, StatusCodes.Ok, cancellationToken);

        return Transition(operation, ServiceResult<MultiplayerSessionDto>.Success(dto));
    }

    /// <summary>
    /// Who may read a session: anyone who has held a seat (see <see cref="IsMemberAsync"/>), and a
    /// player a rematch holds a place for — who has to be able to watch the room come up before they
    /// can join it. Removal takes both away.
    /// </summary>
    private async Task<bool> CanReadAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        await IsMemberAsync(userId, sessionId, cancellationToken)
        || (await IsReservedForAsync(userId, sessionId, cancellationToken)
            && !await IsRemovedAsync(userId, sessionId, cancellationToken));

    /// <summary>Whether a reserved session holds a place for this account.</summary>
    private Task<bool> IsReservedForAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionReservations
            .AsNoTracking()
            .AnyAsync(r => r.SessionId == sessionId && r.UserId == userId, cancellationToken);

    // ---- join-code rotation ----------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> RotateJoinCodeAsync(
        Guid userId,
        Guid sessionId,
        RotateJoinCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        // The code this request already minted, if it ran before — handing out a third code on a
        // retry would strand the friend the host read the second one to.
        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.RotateJoinCode, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await ReadSessionAsync(sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (RefuseRotation(session, userId) is { } refused)
            return Transition(MultiplayerOperations.RotateJoinCode, refused);

        // One guarded UPDATE: host, private and pre-match are re-checked by the statement itself, so a
        // host transfer or a start landing between the read above and here refuses instead of
        // rotating a code the caller no longer controls. A new code colliding with a live session's
        // is absorbed by minting another; the alphabet makes three in a row astronomically unlikely.
        var rotated = false;

        for (var attempt = 0; attempt < 3 && !rotated; attempt++)
        {
            var code = GenerateJoinCode();

            try
            {
                rotated = await _dbContext.MultiplayerSessions
                    .Where(s => s.Id == sessionId
                                && s.HostUserId == userId
                                && s.Visibility == SessionVisibility.Private
                                && (s.State == MultiplayerSessionState.Creating || s.State == MultiplayerSessionState.Created))
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.JoinCode, code), cancellationToken) > 0;
            }
            catch (Exception exception) when (IsUniqueViolation(exception) && attempt < 2)
            {
                // The third collision in a row is not absorbed: at 32^6 codes it is a fault, not luck.
                continue;
            }

            if (!rotated)
            {
                var current = await ReadSessionAsync(sessionId, cancellationToken);

                return Transition(MultiplayerOperations.RotateJoinCode, current is null
                    ? SessionNotFound<MultiplayerSessionDto>(sessionId)
                    : RefuseRotation(current, userId) ?? InvalidTransition<MultiplayerSessionDto>(current.State));
            }
        }

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.RotateJoinCode, sessionId, dto, StatusCodes.Ok, cancellationToken);

        _logger.LogInformation("Join code of multiplayer session {SessionId} rotated by host {UserId}.", sessionId, userId);

        return Transition(MultiplayerOperations.RotateJoinCode, ServiceResult<MultiplayerSessionDto>.Success(dto));
    }

    /// <summary>Why this caller may not rotate this session's code as it stands, or null.</summary>
    private static ServiceResult<MultiplayerSessionDto>? RefuseRotation(MultiplayerSession session, Guid userId) =>
        session switch
        {
            _ when session.HostUserId != userId => NotSessionHost<MultiplayerSessionDto>(),
            _ when session.State.IsTerminal() => ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionClosed, ServiceErrorKind.Conflict, $"Session {session.Id} has already ended."),
            _ when session.Visibility != SessionVisibility.Private => ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.ValidationFailed,
                ServiceErrorKind.Validation,
                "Only a private session has a join code."),
            _ when session.State is not (MultiplayerSessionState.Creating or MultiplayerSessionState.Created) =>
                InvalidTransition<MultiplayerSessionDto>(session.State),
            _ => null
        };

    // ---- close ---------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> CloseAsync(
        Guid userId,
        Guid sessionId,
        CloseMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.Close, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await ReadSessionAsync(sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (session.HostUserId != userId)
            return Transition(MultiplayerOperations.Close, NotSessionHost<MultiplayerSessionDto>());

        // A client can truthfully say it closed the session, or that the room emptied. It cannot
        // claim to be the sweeper or an admin, so anything else is recorded as what it actually is.
        var reason = request.Reason is SessionClosedReason.Empty
            ? SessionClosedReason.Empty
            : SessionClosedReason.HostClosed;

        // Guarded on the caller still being host: a host that lost authority between the read above
        // and the close must not be able to end a match it no longer runs.
        if (await ApplyCloseAsync(sessionId, reason, requiredHostUserId: userId, cancellationToken) == CloseOutcome.NotHost)
            return Transition(MultiplayerOperations.Close, NotSessionHost<MultiplayerSessionDto>());

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Close, sessionId, dto, StatusCodes.Ok, cancellationToken);

        return Transition(MultiplayerOperations.Close, ServiceResult<MultiplayerSessionDto>.Success(dto));
    }

    /// <summary>What <see cref="ApplyCloseAsync"/> actually did.</summary>
    internal enum CloseOutcome
    {
        /// <summary>This call ended the session.</summary>
        Closed,

        /// <summary>It had already ended, and keeps the terms it ended on.</summary>
        AlreadyEnded,

        /// <summary>A host was required and the caller is no longer it. Nothing changed.</summary>
        NotHost
    }

    /// <summary>
    /// Ends a session and releases every seat in it, in one transaction.
    /// <para>
    /// Shared with the admin surface, which closes on different terms and answers to a different
    /// caller but must end a session in exactly the same shape. **Two implementations of "close" is
    /// how one of them ends up forgetting to release the memberships** — and a terminal session with
    /// members still seated locks those accounts out of every future match.
    /// </para>
    /// <para>
    /// **Absorbing.** A closed session stays closed on the terms it closed on: the move is guarded on
    /// the session still being live, so <c>EndedAtUtc</c> and <c>ClosedReason</c> are never moved by a
    /// later call, and neither a retry nor an admin can rewrite why a match originally ended.
    /// </para>
    /// </summary>
    internal async Task<CloseOutcome> ApplyCloseAsync(
        Guid sessionId,
        SessionClosedReason reason,
        Guid? requiredHostUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var closed = await CloseLockedAsync(sessionId, reason, now, cancellationToken, requiredHostUserId);

        if (closed)
        {
            await transaction.CommitAsync(cancellationToken);

            MultiplayerMetrics.SessionsEnded.Add(1, MultiplayerMetrics.Tag("reason", reason.ToString()));
            _logger.LogInformation("Multiplayer session {SessionId} closed ({Reason}).", sessionId, reason);

            return CloseOutcome.Closed;
        }

        await transaction.RollbackAsync(cancellationToken);

        var current = await ReadSessionAsync(sessionId, cancellationToken);

        return current is null || current.State.IsTerminal()
            ? CloseOutcome.AlreadyEnded
            : CloseOutcome.NotHost;
    }

    /// <summary>
    /// The close itself, inside a transaction the caller owns: the session row moves first, then
    /// every seat in it is released. Returns false when the session was already terminal — or, with
    /// <paramref name="requiredHostUserId"/>, no longer hosted by that account — and nothing changed.
    /// </summary>
    private async Task<bool> CloseLockedAsync(
        Guid sessionId,
        SessionClosedReason reason,
        DateTime now,
        CancellationToken cancellationToken,
        Guid? requiredHostUserId = null,
        bool onlyIfUnstarted = false)
    {
        var target = _dbContext.MultiplayerSessions
            .Where(s => s.Id == sessionId
                        && s.State != MultiplayerSessionState.Closed
                        && s.State != MultiplayerSessionState.Failed
                        && s.State != MultiplayerSessionState.Abandoned);

        if (requiredHostUserId is { } host)
            target = target.Where(s => s.HostUserId == host);

        if (onlyIfUnstarted)
            target = target.Where(s => s.StartedAtUtc == null);

        var moved = await target.ExecuteUpdateAsync(set => set
                .SetProperty(s => s.State, MultiplayerSessionState.Closed)
                .SetProperty(s => s.ClosedReason, reason)
                .SetProperty(s => s.EndedAtUtc, now)
                .SetProperty(s => s.CurrentPlayerCount, 0),
            cancellationToken);

        if (moved == 0)
            return false;

        await _dbContext.MultiplayerSessionPlayers
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Status, SessionPlayerStatus.Left)
                    .SetProperty(p => p.LeftAtUtc, now),
                cancellationToken);

        return true;
    }

    // ---- heartbeat -----------------------------------------------------------------------------

    public async Task<ServiceResult<HeartbeatResponse>> HeartbeatAsync(
        Guid userId,
        Guid sessionId,
        HeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await HeartbeatCoreAsync(userId, sessionId, request, cancellationToken);

        MultiplayerMetrics.Heartbeats.Add(1, MultiplayerMetrics.Tag("outcome", MultiplayerMetrics.Outcome(result)));

        return result;
    }

    private async Task<ServiceResult<HeartbeatResponse>> HeartbeatCoreAsync(
        Guid userId,
        Guid sessionId,
        HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var session = await ReadSessionAsync(sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<HeartbeatResponse>(sessionId);

        // **This refusal is the migration safety net.** A host that dropped out, lost authority to a
        // member, and then came back finds itself refused here — so it cannot keep a session alive,
        // restart it, or close it. Losing the host role is something it learns from this 403 rather
        // than from anything the transport tells it.
        if (session.HostUserId != userId)
            return NotSessionHost<HeartbeatResponse>();

        var now = DateTime.UtcNow;
        var live = !session.State.IsTerminal();

        if (live)
        {
            // A terminal session is not resurrected by a heartbeat arriving late — hence the state in
            // the guard. And the host in it: authority can move between the read above and here.
            var advanced = await _dbContext.MultiplayerSessions
                .Where(s => s.Id == sessionId
                            && s.HostUserId == userId
                            && s.State != MultiplayerSessionState.Closed
                            && s.State != MultiplayerSessionState.Failed
                            && s.State != MultiplayerSessionState.Abandoned)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastHeartbeatAtUtc, now), cancellationToken);

            if (advanced == 0)
            {
                var current = await ReadSessionAsync(sessionId, cancellationToken);

                if (current is not null && current.HostUserId != userId)
                    return NotSessionHost<HeartbeatResponse>();

                // Otherwise it went terminal in the gap. Report that below rather than refusing.
                live = false;
            }
        }

        if (live)
            await ReconcileRosterAsync(userId, sessionId, request, now, cancellationToken);

        var reread = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Include(s => s.Players)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (reread is null)
            return SessionNotFound<HeartbeatResponse>(sessionId);

        var seated = reread.Players.Where(p => !p.Status.HasDeparted()).OrderBy(p => p.Slot).ToList();
        var names = await _names.ResolveAsync(seated.Select(p => p.UserId).ToList(), cancellationToken);

        return ServiceResult<HeartbeatResponse>.Success(new HeartbeatResponse
        {
            State = reread.State,
            ServerTimeUtc = now,
            NextHeartbeatInSeconds = _options.HeartbeatIntervalSeconds,
            Players = seated.Select(p => p.ToDto(names.GetValueOrDefault(p.UserId))).ToList()
        });
    }

    /// <summary>
    /// Folds the host's view of who is in the room into the roster.
    /// <para>
    /// **Presence, not membership.** Ids the host reports that are not already seated change nothing —
    /// honouring them would let a client seat arbitrary accounts by naming them, which is the one thing
    /// the roster must never be able to do. They are counted in the log, because a host naming
    /// strangers is either a bug or a probe.
    /// </para>
    /// <para>
    /// **The host is present by definition** — it is the one heartbeating. Counting it as seen whether
    /// or not it lists itself is what stops a host from being marked missing, and eventually released,
    /// while it is demonstrably alive and still holding authority.
    /// </para>
    /// <para>
    /// Every write repeats the condition it was decided on, so a member who reconnected in the gap
    /// between this read and these writes is not marked missing over the top of it.
    /// </para>
    /// </summary>
    private async Task ReconcileRosterAsync(
        Guid hostId,
        Guid sessionId,
        HeartbeatRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var reported = request.ConnectedUserIds.Take(MaxReportedPerHeartbeat).ToHashSet();
        reported.Add(hostId);

        var seated = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .Select(p => new { p.Id, p.UserId, p.Status, p.LastSeenAtUtc })
            .ToListAsync(cancellationToken);

        var strangers = reported.Count(id => seated.All(p => p.UserId != id));

        if (strangers > 0)
            _logger.LogInformation(
                "Heartbeat for multiplayer session {SessionId} named {Count} account(s) with no seat; ignored.",
                sessionId, strangers);

        var seen = seated.Where(p => reported.Contains(p.UserId)).Select(p => p.Id).ToList();

        if (seen.Count > 0)
        {
            await _dbContext.MultiplayerSessionPlayers
                .Where(p => seen.Contains(p.Id)
                            && p.Status != SessionPlayerStatus.Left
                            && p.Status != SessionPlayerStatus.Removed)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.LastSeenAtUtc, now), cancellationToken);

            await _dbContext.MultiplayerSessionPlayers
                .Where(p => seen.Contains(p.Id)
                            && (p.Status == SessionPlayerStatus.Joined || p.Status == SessionPlayerStatus.Disconnected))
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, SessionPlayerStatus.Connected), cancellationToken);
        }

        // Missing, not gone. They keep their seat for the rest of the grace period, and only the
        // sweeper ever promotes this to Left — a host that briefly cannot see a peer must not be able
        // to evict them.
        var droppedCutoff = now.AddSeconds(-_options.PlayerDisconnectGraceSeconds);

        var dropped = seated
            .Where(p => !reported.Contains(p.UserId)
                        && p.Status == SessionPlayerStatus.Connected
                        && p.LastSeenAtUtc < droppedCutoff)
            .Select(p => p.Id)
            .ToList();

        if (dropped.Count > 0)
            await _dbContext.MultiplayerSessionPlayers
                .Where(p => dropped.Contains(p.Id)
                            && p.Status == SessionPlayerStatus.Connected
                            && p.LastSeenAtUtc < droppedCutoff)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, SessionPlayerStatus.Disconnected), cancellationToken);

        // Seated by the backend and never once seen in the room. Measured from the seat — their
        // last-seen time is the moment they joined — over the longer connect grace, because loading
        // a scene and reaching the transport is the slow half of joining on a cheap phone.
        var neverArrivedCutoff = now.AddSeconds(-Math.Max(_options.JoinedConnectGraceSeconds, _options.PlayerDisconnectGraceSeconds));

        var neverArrived = seated
            .Where(p => !reported.Contains(p.UserId)
                        && p.Status == SessionPlayerStatus.Joined
                        && p.LastSeenAtUtc < neverArrivedCutoff)
            .Select(p => p.Id)
            .ToList();

        if (neverArrived.Count > 0)
            await _dbContext.MultiplayerSessionPlayers
                .Where(p => neverArrived.Contains(p.Id)
                            && p.Status == SessionPlayerStatus.Joined
                            && p.LastSeenAtUtc < neverArrivedCutoff)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, SessionPlayerStatus.Disconnected), cancellationToken);
    }

    // ---- host transfer -------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> TransferHostAsync(
        Guid userId,
        Guid sessionId,
        TransferHostRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await ReplayAsync<MultiplayerSessionDto>(userId, requestId, MultiplayerOperations.HostTransfer, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await ReadSessionAsync(sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (session.State.IsTerminal())
            return Transition(MultiplayerOperations.HostTransfer, ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionClosed,
                ServiceErrorKind.Conflict,
                $"Session {sessionId} has already ended."));

        var seated = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .Select(p => new { p.UserId, p.LastSeenAtUtc })
            .ToListAsync(cancellationToken);

        if (seated.All(p => p.UserId != request.ToUserId))
            return Transition(MultiplayerOperations.HostTransfer, ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.NotSessionMember,
                ServiceErrorKind.Forbidden,
                $"User {request.ToUserId} does not hold a seat in session {sessionId}."));

        var observedHost = session.HostUserId;

        if (observedHost != userId)
        {
            // An involuntary claim. Two things have to hold: the current host really has gone quiet,
            // and the claimant is naming **themselves**. Allowing a third party to be installed by
            // someone who is not the host would make authority transferable by any member at will.
            if (request.ToUserId != userId)
                return Transition(MultiplayerOperations.HostTransfer, NotSessionHost<MultiplayerSessionDto>());

            var hostLastSeen = seated.FirstOrDefault(p => p.UserId == observedHost)?.LastSeenAtUtc
                               ?? session.LastHeartbeatAtUtc;
            var claimCutoff = DateTime.UtcNow.AddSeconds(-_options.HostClaimGraceSeconds);

            if (hostLastSeen >= claimCutoff)
                return Transition(MultiplayerOperations.HostTransfer, ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.HostStillActive,
                    ServiceErrorKind.Conflict,
                    "The current host is still within its grace period.",
                    new Dictionary<string, object?>
                    {
                        ["hostLastSeenAtUtc"] = hostLastSeen,
                        ["hostClaimGraceSeconds"] = _options.HostClaimGraceSeconds
                    }));
        }

        // Already there. Not an error — a client retrying an unacknowledged claim asked for a state
        // that now holds.
        if (observedHost == request.ToUserId)
            return Transition(MultiplayerOperations.HostTransfer,
                ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken)));

        var target = request.ToUserId;
        int moved;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // **Compare-and-swap on the host itself.** Two members claiming a vacant host both
            // observed the same host; the first UPDATE replaces it and the second no longer matches.
            // Exactly one winner — and, unlike a row-version guard, a heartbeat or a join landing in
            // between cannot make the only claimant lose to nobody. The target must still hold a
            // seat at the moment of the swap, or a member who left in the gap could be installed.
            moved = await _dbContext.MultiplayerSessions
                .Where(s => s.Id == sessionId
                            && s.HostUserId == observedHost
                            && s.State != MultiplayerSessionState.Closed
                            && s.State != MultiplayerSessionState.Failed
                            && s.State != MultiplayerSessionState.Abandoned
                            && _dbContext.MultiplayerSessionPlayers.Any(p =>
                                p.SessionId == sessionId
                                && p.UserId == target
                                && p.Status != SessionPlayerStatus.Left
                                && p.Status != SessionPlayerStatus.Removed))
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.HostUserId, target), cancellationToken);

            if (moved == 1)
            {
                // Both sides move inside the one transaction, so the roster and the session can never
                // disagree about who is in charge.
                await SetHostFlagsAsync(sessionId, target, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        if (moved == 0)
        {
            // The loser re-reads and reports what actually happened rather than retrying into a
            // second migration — two clients ping-ponging authority is far worse than one losing a race.
            var winner = await ReadSessionAsync(sessionId, cancellationToken);

            if (winner is null)
                return SessionNotFound<MultiplayerSessionDto>(sessionId);

            if (winner.HostUserId == target)
                return Transition(MultiplayerOperations.HostTransfer,
                    ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken)));

            if (winner.State.IsTerminal())
                return Transition(MultiplayerOperations.HostTransfer, ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.SessionClosed,
                    ServiceErrorKind.Conflict,
                    $"Session {sessionId} has already ended."));

            return Transition(MultiplayerOperations.HostTransfer, ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.HostStillActive,
                ServiceErrorKind.Conflict,
                "Another claim reached the session first.",
                new Dictionary<string, object?> { ["hostUserId"] = winner.HostUserId }));
        }

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.HostTransfer, sessionId, dto, StatusCodes.Ok, cancellationToken);

        _logger.LogInformation(
            "Multiplayer session {SessionId} host moved {From} -> {To} ({Kind}).",
            sessionId, observedHost, target, observedHost == userId ? "voluntary" : "claim");

        return Transition(MultiplayerOperations.HostTransfer, ServiceResult<MultiplayerSessionDto>.Success(dto));
    }

    // ---- reads ---------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> GetAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanReadAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        return ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken));
    }

    public async Task<ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>> GetPlayersAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanReadAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<IReadOnlyList<MultiplayerSessionPlayerDto>>(sessionId);

        var players = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .OrderBy(p => p.Slot)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(players.Select(p => p.UserId).ToList(), cancellationToken);

        return ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>.Success(
            players.Select(p => p.ToDto(names.GetValueOrDefault(p.UserId))).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<MultiplayerSessionDto>>> ListForUserAsync(
        Guid userId,
        MultiplayerSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        // Scoped to the caller's own memberships, always. This is "where am I?" after a crash —
        // discovery is Photon's room list, not ours.
        var sessions = _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Include(s => s.Players)
            .Where(s => s.Players.Any(p => p.UserId == userId
                                           && p.Status != SessionPlayerStatus.Left
                                           && p.Status != SessionPlayerStatus.Removed));

        if (query.GameId is { } gameId)
            sessions = sessions.Where(s => s.GameId == gameId);

        if (query.State is { } state)
            sessions = sessions.Where(s => s.State == state);

        if (query.Visibility is { } visibility)
            sessions = sessions.Where(s => s.Visibility == visibility);

        if (query.IsRanked is { } isRanked)
            sessions = sessions.Where(s => s.IsRanked == isRanked);

        if (query.LessonId is { } lessonId)
            sessions = sessions.Where(s => s.LessonId == lessonId);

        var rows = await sessions
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(
            rows.SelectMany(s => s.Players).Where(p => !p.Status.HasDeparted()).Select(p => p.UserId).ToList(),
            cancellationToken);

        var now = DateTime.UtcNow;

        return ServiceResult<IReadOnlyList<MultiplayerSessionDto>>.Success(
            rows.Select(s => s.ToDto(names, now)).ToList());
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<MultiplayerSessionDto> BuildAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Include(s => s.Players)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session {sessionId} could not be read back after a write.");

        var names = await _names.ResolveAsync(
            session.Players.Where(p => !p.Status.HasDeparted()).Select(p => p.UserId).ToList(),
            cancellationToken);

        return session.ToDto(names, DateTime.UtcNow);
    }

    private Task<MultiplayerSession?> ReadSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    /// <summary>What a transaction learns from locking the session row: who hosts it and where it is.</summary>
    private sealed record SessionLock(Guid HostUserId, MultiplayerSessionState State);

    /// <summary>
    /// Takes the session row's update lock, held to the end of the enclosing transaction, and reads it.
    /// **The first statement of every transaction that touches a session's memberships**, so they all
    /// queue on the same row in the same order instead of deadlocking across it.
    /// </summary>
    private Task<SessionLock?> LockSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessions
            .FromSqlRaw("SELECT * FROM [MultiplayerSessions] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", sessionId)
            .AsNoTracking()
            .Select(s => new SessionLock(s.HostUserId, s.State))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Marks exactly one seated member as host, in the roster, to match the session row.</summary>
    private Task<int> SetHostFlagsAsync(Guid sessionId, Guid hostUserId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionPlayers
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsHost, p => p.UserId == hostUserId), cancellationToken);

    /// <summary>
    /// Has the caller ever held a seat in this session? This is the **read authorization** check,
    /// and it deliberately ignores whether the seat is still held.
    /// <para>
    /// Requiring a live seat here looked right and was wrong in two ways that matter. Closing a
    /// session marks every membership departed, so the host would be locked out of the session they
    /// had just closed — a second, idempotent close would answer 404 instead of 200. And a client
    /// that wants to read the final state of a match it just finished would be refused the record of
    /// its own game.
    /// </para>
    /// <para>
    /// Nothing leaks: a row only exists for someone who was genuinely in the session, and a stranger
    /// still gets the same 404 they always did.
    /// </para>
    /// <para>
    /// **Except for someone the host removed**, who loses sight of the room entirely — roster, state,
    /// everything. Being removed from a lobby and then being able to keep watching who is in it is
    /// exactly what removal is supposed to stop.
    /// </para>
    /// </summary>
    private Task<bool> IsMemberAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .AnyAsync(p => p.SessionId == sessionId
                           && p.UserId == userId
                           && !_dbContext.MultiplayerSessionBans.Any(b => b.SessionId == sessionId && b.UserId == userId),
                cancellationToken);

    /// <summary>Whether the host removed this account from this session.</summary>
    private Task<bool> IsRemovedAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionBans
            .AsNoTracking()
            .AnyAsync(b => b.SessionId == sessionId && b.UserId == userId, cancellationToken);

    /// <summary>Does the caller hold a seat in any session that has not ended?</summary>
    internal Task<bool> HasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId
                           && p.Status != SessionPlayerStatus.Left
                           && p.Status != SessionPlayerStatus.Removed
                           && p.Session!.State != MultiplayerSessionState.Closed
                           && p.Session.State != MultiplayerSessionState.Failed
                           && p.Session.State != MultiplayerSessionState.Abandoned,
                cancellationToken);

    private Task<bool> TransportNameIsTakenAsync(string transportName, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessions
            .AsNoTracking()
            .AnyAsync(s => s.TransportSessionName == transportName
                           && s.State != MultiplayerSessionState.Closed
                           && s.State != MultiplayerSessionState.Failed
                           && s.State != MultiplayerSessionState.Abandoned,
                cancellationToken);

    private bool IsAcceptedProtocol(int protocolVersion) =>
        _options.EffectiveProtocolVersions.Contains(protocolVersion);

    /// <summary>The stored answer to an operation this key already completed, counted when there is one.</summary>
    private async Task<T?> ReplayAsync<T>(Guid userId, string requestId, string operation, CancellationToken cancellationToken)
        where T : class
    {
        var replayed = await _log.TryReplayAsync<T>(userId, requestId, operation, cancellationToken);

        if (replayed is not null)
            MultiplayerMetrics.Replays.Add(1, MultiplayerMetrics.Tag("operation", operation));

        return replayed;
    }

    /// <summary>
    /// A refusal — unless a duplicate of this very request, still in flight when the log was first
    /// checked, has finished since and is the reason for it. A retry that overlapped its original is
    /// owed the original's answer, not an <c>ALREADY_IN_SESSION</c> caused by itself.
    /// </summary>
    private async Task<ServiceResult<T>> ReplayOrAsync<T>(
        Guid userId,
        string requestId,
        string operation,
        ServiceResult<T> refusal,
        CancellationToken cancellationToken) where T : class =>
        await ReplayAsync<T>(userId, requestId, operation, cancellationToken) is { } replayed
            ? ServiceResult<T>.Success(replayed)
            : refusal;

    /// <summary>Lowest unoccupied seat. Seats freed by a departure are reused before new ones.</summary>
    private static int FirstFreeSlot(IReadOnlyCollection<int> taken)
    {
        for (var slot = 0; slot < taken.Count; slot++)
        {
            if (!taken.Contains(slot))
                return slot;
        }

        return taken.Count;
    }

    /// <summary>
    /// A join code a child can read off a screen and type. <c>I</c>, <c>O</c>, <c>0</c> and <c>1</c>
    /// are left out because they are the pairs people mistype, and a wrong code is indistinguishable
    /// from a session that has ended.
    /// <para>
    /// From the cryptographic generator, not <c>Random</c>: a code is the only thing standing between
    /// a private room and a stranger who can guess what comes next.
    /// </para>
    /// </summary>
    private static string GenerateJoinCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        return string.Create(6, alphabet, (span, source) =>
        {
            for (var index = 0; index < span.Length; index++)
                span[index] = source[RandomNumberGenerator.GetInt32(source.Length)];
        });
    }

    /// <summary>
    /// Drops everything the failed attempt staged. Without this the next SaveChanges on the same
    /// scoped context would retry the very insert that just failed.
    /// </summary>
    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>A unique index refused the write — from SaveChanges (wrapped) or ExecuteUpdate (raw).</summary>
    private static bool IsUniqueViolation(Exception exception) =>
        (exception as SqlException ?? exception.InnerException as SqlException) is { Number: 2601 or 2627 };

    private static ServiceResult<T> Transition<T>(string operation, ServiceResult<T> result)
    {
        MultiplayerMetrics.Transitions.Add(1,
            MultiplayerMetrics.Tag("operation", operation),
            MultiplayerMetrics.Tag("outcome", MultiplayerMetrics.Outcome(result)));

        return result;
    }

    // ---- refusals ------------------------------------------------------------------------------
    //
    // Generic in the payload type because the same refusals have to be returned from methods that
    // answer with a session, with a roster, and with a heartbeat. One definition per refusal is what
    // keeps the code and the messageKey from drifting apart across three copies.

    private static ServiceResult<T> SessionNotFound<T>(Guid sessionId) =>
        ServiceResult<T>.Failure(
            ApiErrors.SessionNotFound,
            ServiceErrorKind.NotFound,
            $"Session {sessionId} does not exist, or the caller is not a member of it.");

    private static ServiceResult<T> NotSessionHost<T>() =>
        ServiceResult<T>.Failure(
            ApiErrors.NotSessionHost,
            ServiceErrorKind.Forbidden,
            "Only the current host may perform this operation.");

    private static ServiceResult<T> AlreadyInSession<T>() =>
        ServiceResult<T>.Failure(
            ApiErrors.AlreadyInSession,
            ServiceErrorKind.Conflict,
            "The caller already holds a seat in a session that has not ended.");

    private static ServiceResult<T> InvalidTransition<T>(MultiplayerSessionState from) =>
        ServiceResult<T>.Failure(
            ApiErrors.SessionInvalidTransition,
            ServiceErrorKind.Conflict,
            $"The requested move is not legal from {from}.");

    private static ServiceResult<MultiplayerSessionDto> BelowMinPlayers(int current, int min) =>
        ServiceResult<MultiplayerSessionDto>.Failure(
            ApiErrors.SessionBelowMinPlayers,
            ServiceErrorKind.Conflict,
            $"Session has {current} of the {min} players it needs.",
            new Dictionary<string, object?>
            {
                ["currentPlayerCount"] = current,
                ["minPlayers"] = min
            });

    private ServiceResult<T> ProtocolMismatch<T>(int requested) =>
        ServiceResult<T>.Failure(
            ApiErrors.ProtocolVersionMismatch,
            ServiceErrorKind.Validation,
            $"Protocol version {requested} is not accepted by this server.",
            new Dictionary<string, object?>
            {
                ["requested"] = requested,
                ["accepted"] = _options.EffectiveProtocolVersions
            });

    /// <summary>The status codes written into the request log, so a replay reports what the first call did.</summary>
    private static class StatusCodes
    {
        public const int Ok = 200;
        public const int Created = 201;
    }
}
