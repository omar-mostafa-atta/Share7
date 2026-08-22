using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// The session lifecycle. See <see cref="IMultiplayerSessionService"/> for the contract.
/// <para>
/// Two rules shape everything below. **Capacity and membership are decided by the database, not by
/// this class** — a conditional UPDATE and two filtered unique indexes, so the guarantees survive
/// requests that arrive in the same millisecond. And **the caller is always the JWT subject**: no
/// method reads an identity out of a request body, so impersonation is not something checked for
/// here, it is something that cannot be expressed.
/// </para>
/// </summary>
public class MultiplayerSessionService : IMultiplayerSessionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerRequestLogStore _log;
    private readonly MultiplayerOptions _options;

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

    public MultiplayerSessionService(
        ApplicationDbContext dbContext,
        MultiplayerRequestLogStore log,
        IOptions<MultiplayerOptions> options)
    {
        _dbContext = dbContext;
        _log = log;
        _options = options.Value;
    }

    // ---- create --------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> CreateAsync(
        Guid userId,
        CreateMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await _log.TryReplayAsync<MultiplayerSessionDto>(
                userId, requestId, MultiplayerOperations.Create, cancellationToken) is { } replayed)
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

        if (await HasActiveMembershipAsync(userId, cancellationToken))
            return AlreadyInSession<MultiplayerSessionDto>();

        // The catalog is authoritative for seat counts, and these are **copied** rather than read
        // through — editing the game row later must not resize a match that is already running.
        var catalogMax = Math.Max(1, game.MaxPlayers);
        var catalogMin = Math.Clamp(game.MinPlayers, 1, catalogMax);
        var maxPlayers = Math.Clamp(request.MaxPlayers ?? catalogMax, catalogMin, catalogMax);

        var visibility = request.Visibility is SessionVisibility.Private
            ? SessionVisibility.Private
            : SessionVisibility.Public;

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
            MinPlayers = catalogMin,

            // The host occupies a seat from the instant the row exists.
            CurrentPlayerCount = 1,
            ProtocolVersion = request.ProtocolVersion,
            CurriculumPathJson = MultiplayerMappings.SerializePath(request.CurriculumPath),
            LessonId = request.CurriculumPath?.LessonId,
            IsRanked = request.IsRanked,
            CreatedAtUtc = now,
            LastHeartbeatAtUtc = now
        };

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

        try
        {
            // One SaveChanges, so one transaction: **a session can never exist without its host.**
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            Detach();

            // Which index bit is worked out by re-reading rather than by parsing the SQL error
            // text — the message format is not a contract, and the answer is cheap to look up.
            if (await TransportNameIsTakenAsync(transportName, cancellationToken))
                return ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.TransportNameTaken,
                    ServiceErrorKind.Conflict,
                    $"Transport session name '{transportName}' is already in use by a live session.");

            if (await HasActiveMembershipAsync(userId, cancellationToken))
                return AlreadyInSession<MultiplayerSessionDto>();

            // Only the join code is left, and it is server-minted — a collision is ours to absorb,
            // not the caller's to see.
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.TransportNameTaken,
                ServiceErrorKind.Conflict,
                "Session could not be created because of a key collision. Retry with a new name.");
        }

        var dto = await BuildAsync(session.Id, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Create, session.Id, dto, StatusCodes.Created, cancellationToken);

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

        if (await _log.TryReplayAsync<MultiplayerSessionDto>(
                userId, requestId, MultiplayerOperations.Start, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await _dbContext.MultiplayerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (session.HostUserId != userId)
            return NotSessionHost<MultiplayerSessionDto>();

        var now = DateTime.UtcNow;

        switch (session.State)
        {
            // The confirmation half of the create saga: the transport room came up, so the session
            // becomes joinable. Until this lands nobody can get in, which is what stops players
            // being seated into a room that never existed.
            case MultiplayerSessionState.Creating:
                session.State = MultiplayerSessionState.Created;
                break;

            // The commit-to-start. Joins close and the clock starts. `Starting` is a real stored
            // state and a legal transition, but a single call carries the session all the way to
            // Running — there is no second request for the server to wait for, so lingering in
            // Starting would only create a window in which nothing can happen.
            case MultiplayerSessionState.Created:
                if (session.CurrentPlayerCount < session.MinPlayers)
                    return ServiceResult<MultiplayerSessionDto>.Failure(
                        ApiErrors.SessionBelowMinPlayers,
                        ServiceErrorKind.Conflict,
                        $"Session has {session.CurrentPlayerCount} of the {session.MinPlayers} players it needs.",
                        new Dictionary<string, object?>
                        {
                            ["currentPlayerCount"] = session.CurrentPlayerCount,
                            ["minPlayers"] = session.MinPlayers
                        });

                session.State = MultiplayerSessionState.Running;
                session.StartedAtUtc = now;
                break;

            default:
                return InvalidTransition<MultiplayerSessionDto>(session.State);
        }

        session.LastHeartbeatAtUtc = now;

        try
        {
            // RowVersion is the guard: if anything moved this session between the read above and
            // here, the UPDATE matches no row and we refuse rather than overwrite.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Detach();
            return InvalidTransition<MultiplayerSessionDto>(null);
        }

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Start, sessionId, dto, StatusCodes.Ok, cancellationToken);

        return ServiceResult<MultiplayerSessionDto>.Success(dto);
    }

    // ---- join ----------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> JoinAsync(
        Guid userId,
        Guid sessionId,
        JoinMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await _log.TryReplayAsync<MultiplayerSessionDto>(
                userId, requestId, MultiplayerOperations.Join, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var result = await SeatAsync(userId, sessionId, request.ProtocolVersion, cancellationToken);

        if (result.Succeeded && result.Value is { } seated)
            await _log.RecordAsync(
                userId, requestId, MultiplayerOperations.Join, sessionId, seated, StatusCodes.Ok, cancellationToken);

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
        if (!IsAcceptedProtocol(protocolVersion))
            return ProtocolMismatch<MultiplayerSessionDto>(protocolVersion);

        // One account plays one match at a time. **Within a session the filtered unique index
        // enforces this**; across sessions it cannot, because no single index spans them — so this
        // read is the check for the cross-session case, and it is genuinely racy. Two joins to two
        // different sessions in the same instant can both pass. The consequence is a player seated
        // twice in different rooms, which the client resolves by leaving one; it is not a
        // correctness hazard for either session's capacity.
        if (await HasActiveMembershipAsync(userId, cancellationToken))
            return AlreadyInSession<MultiplayerSessionDto>();

        for (var attempt = 0; attempt < JoinAttempts; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            // **The whole capacity guarantee is this one statement.** Two clients racing for the
            // last seat both run it; exactly one finds CurrentPlayerCount < MaxPlayers still true
            // and the other affects zero rows. No SELECT takes part in the decision, so READ
            // COMMITTED is sufficient and there is no lock ordering to get wrong.
            var rows = await _dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE [MultiplayerSessions]
                SET [CurrentPlayerCount] = [CurrentPlayerCount] + 1
                WHERE [Id] = {0}
                  AND [State] = {1}
                  AND [CurrentPlayerCount] < [MaxPlayers]
                  AND [ProtocolVersion] = {2}
                """,
                [sessionId, WireEnum.ToWire(MultiplayerSessionState.Created), protocolVersion],
                cancellationToken);

            if (rows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                var refusal = await ClassifyFailedSeatAsync(sessionId, protocolVersion, cancellationToken);

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
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // Rolling back is what makes the increment above safe: the seat is released with
                // the same transaction that took it, so a failed insert can never leave a session
                // reporting a player it does not have.
                await transaction.RollbackAsync(cancellationToken);
                Detach();

                if (await HasActiveMembershipAsync(userId, cancellationToken))
                    return AlreadyInSession<MultiplayerSessionDto>();

                // Otherwise two joiners picked the same free seat. Re-read and take another.
                continue;
            }

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
        int protocolVersion,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

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

        if (await _log.TryReplayAsync<MultiplayerSessionDto>(
                userId, requestId, MultiplayerOperations.Leave, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await _dbContext.MultiplayerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        var membership = await _dbContext.MultiplayerSessionPlayers
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.UserId == userId, cancellationToken);

        if (membership is null)
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        // **Idempotent.** Already gone, or the session already ended — either way the caller's
        // intent holds and there is nothing to undo. A second leave is not an error; it is a retry.
        if (membership.Status.HasDeparted() || session.State.IsTerminal())
            return ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken));

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        membership.Status = SessionPlayerStatus.Left;
        membership.LeftAtUtc = now;
        membership.IsHost = false;

        var remaining = await _dbContext.MultiplayerSessionPlayers
            .Where(p => p.SessionId == sessionId
                        && p.UserId != userId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .OrderBy(p => p.Slot)
            .ToListAsync(cancellationToken);

        if (remaining.Count == 0)
        {
            // Nobody left to play. Closing it here rather than waiting for the sweeper means the
            // transport name is released immediately, so the same host can start again at once.
            session.State = MultiplayerSessionState.Closed;
            session.ClosedReason = SessionClosedReason.Empty;
            session.EndedAtUtc = now;
            session.CurrentPlayerCount = 0;
        }
        else
        {
            if (session.HostUserId == userId)
            {
                // Authority passes to the lowest seat still occupied — deterministic, so every
                // client can predict the same successor rather than waiting to be told.
                var successor = remaining.FirstOrDefault(p => p.Status == SessionPlayerStatus.Connected)
                                ?? remaining[0];

                session.HostUserId = successor.UserId;
                successor.IsHost = true;
            }

            session.CurrentPlayerCount = remaining.Count;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            Detach();

            // Something else moved the session — most likely the sweeper closing it, or the host
            // migrating. The caller's intent (be out of this session) is satisfied either way.
            return ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken));
        }

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Leave, sessionId, dto, StatusCodes.Ok, cancellationToken);

        return ServiceResult<MultiplayerSessionDto>.Success(dto);
    }

    // ---- close ---------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> CloseAsync(
        Guid userId,
        Guid sessionId,
        CloseMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await _log.TryReplayAsync<MultiplayerSessionDto>(
                userId, requestId, MultiplayerOperations.Close, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await _dbContext.MultiplayerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (session.HostUserId != userId)
            return NotSessionHost<MultiplayerSessionDto>();

        // A client can truthfully say it closed the session, or that the room emptied. It cannot
        // claim to be the sweeper or an admin, so anything else is recorded as what it actually is.
        var reason = request.Reason is SessionClosedReason.Empty
            ? SessionClosedReason.Empty
            : SessionClosedReason.HostClosed;

        var dto = await ApplyCloseAsync(session, reason, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Close, sessionId, dto, StatusCodes.Ok, cancellationToken);

        return ServiceResult<MultiplayerSessionDto>.Success(dto);
    }

    /// <summary>
    /// Ends a session and releases every seat in it, with no authorization of its own.
    /// <para>
    /// Shared with the admin surface, which closes on different terms and answers to a different
    /// caller but must end a session in exactly the same shape. **Two implementations of "close" is
    /// how one of them ends up forgetting to release the memberships** — and a terminal session with
    /// members still seated locks those accounts out of every future match, because the
    /// one-session-at-a-time rule reads membership rather than session state.
    /// </para>
    /// <para>
    /// **Absorbing.** A closed session stays closed on the terms it closed on: <c>EndedAtUtc</c> and
    /// <c>ClosedReason</c> are never moved by a later call, so neither a retry nor an admin can
    /// rewrite why a match originally ended.
    /// </para>
    /// </summary>
    internal async Task<MultiplayerSessionDto> ApplyCloseAsync(
        MultiplayerSession session,
        SessionClosedReason reason,
        CancellationToken cancellationToken)
    {
        if (session.State.IsTerminal())
            return await BuildAsync(session.Id, cancellationToken);

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        session.State = MultiplayerSessionState.Closed;
        session.ClosedReason = reason;
        session.EndedAtUtc = now;
        session.CurrentPlayerCount = 0;

        var seated = await _dbContext.MultiplayerSessionPlayers
            .Where(p => p.SessionId == session.Id
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .ToListAsync(cancellationToken);

        foreach (var player in seated)
        {
            player.Status = SessionPlayerStatus.Left;
            player.LeftAtUtc = now;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            Detach();

            // Lost to the sweeper or to a concurrent close. The session is terminal either way,
            // which is what the caller asked for.
        }

        return await BuildAsync(session.Id, cancellationToken);
    }

    // ---- heartbeat -----------------------------------------------------------------------------

    public async Task<ServiceResult<HeartbeatResponse>> HeartbeatAsync(
        Guid userId,
        Guid sessionId,
        HeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.MultiplayerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<HeartbeatResponse>(sessionId);

        // **This refusal is the migration safety net.** A host that dropped out, lost authority to a
        // member, and then came back finds itself refused here — so it cannot keep a session alive,
        // restart it, or close it. Losing the host role is something it learns from this 403 rather
        // than from anything the transport tells it.
        if (session.HostUserId != userId)
            return NotSessionHost<HeartbeatResponse>();

        var now = DateTime.UtcNow;

        var players = await _dbContext.MultiplayerSessionPlayers
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .OrderBy(p => p.Slot)
            .ToListAsync(cancellationToken);

        // **Presence, not membership.** Ids the host reports that are not already seated are dropped
        // here. Honouring them would let a client seat arbitrary accounts by naming them, which is
        // the one thing the roster must never be able to do.
        var reported = request.ConnectedUserIds.ToHashSet();
        var disconnectCutoff = now.AddSeconds(-_options.PlayerDisconnectGraceSeconds);

        foreach (var player in players)
        {
            if (reported.Contains(player.UserId))
            {
                player.LastSeenAtUtc = now;

                if (player.Status is SessionPlayerStatus.Joined or SessionPlayerStatus.Disconnected)
                    player.Status = SessionPlayerStatus.Connected;
            }
            else if (player.Status == SessionPlayerStatus.Connected && player.LastSeenAtUtc < disconnectCutoff)
            {
                // Missing, not gone. They keep their seat for the rest of the grace period, and only
                // the sweeper ever promotes this to Left — a host that briefly cannot see a peer must
                // not be able to evict them.
                player.Status = SessionPlayerStatus.Disconnected;
            }
        }

        // A terminal session is not resurrected by a heartbeat arriving late. The host is simply told
        // the authoritative state and is expected to tear down.
        if (!session.State.IsTerminal())
            session.LastHeartbeatAtUtc = now;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The sweeper or another writer moved this session mid-heartbeat. The state we report
            // below is re-read, so the host still gets the truth — just not our version of it.
            Detach();
        }

        var current = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Include(s => s.Players)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (current is null)
            return SessionNotFound<HeartbeatResponse>(sessionId);

        var names = await ResolveDisplayNamesAsync(current.Players.Select(p => p.UserId), cancellationToken);

        return ServiceResult<HeartbeatResponse>.Success(new HeartbeatResponse
        {
            State = current.State,
            ServerTimeUtc = now,
            NextHeartbeatInSeconds = _options.HeartbeatIntervalSeconds,
            Players = current.Players
                .Where(p => !p.Status.HasDeparted())
                .OrderBy(p => p.Slot)
                .Select(p => p.ToDto(names.GetValueOrDefault(p.UserId)))
                .ToList()
        });
    }

    // ---- host transfer -------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> TransferHostAsync(
        Guid userId,
        Guid sessionId,
        TransferHostRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        if (await _log.TryReplayAsync<MultiplayerSessionDto>(
                userId, requestId, MultiplayerOperations.HostTransfer, cancellationToken) is { } replayed)
            return ServiceResult<MultiplayerSessionDto>.Success(replayed);

        var session = await _dbContext.MultiplayerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null || !await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        if (session.State.IsTerminal())
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.SessionClosed,
                ServiceErrorKind.Conflict,
                $"Session {sessionId} has already ended.");

        var seated = await _dbContext.MultiplayerSessionPlayers
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .ToListAsync(cancellationToken);

        var target = seated.FirstOrDefault(p => p.UserId == request.ToUserId);

        if (target is null)
            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.NotSessionMember,
                ServiceErrorKind.Forbidden,
                $"User {request.ToUserId} does not hold a seat in session {sessionId}.");

        var callerIsHost = session.HostUserId == userId;

        if (!callerIsHost)
        {
            // An involuntary claim. Two things have to hold: the current host really has gone quiet,
            // and the claimant is naming **themselves**. Allowing a third party to be installed by
            // someone who is not the host would make authority transferable by any member at will.
            if (request.ToUserId != userId)
                return NotSessionHost<MultiplayerSessionDto>();

            var host = seated.FirstOrDefault(p => p.UserId == session.HostUserId);
            var hostLastSeen = host?.LastSeenAtUtc ?? session.LastHeartbeatAtUtc;
            var claimCutoff = DateTime.UtcNow.AddSeconds(-_options.HostClaimGraceSeconds);

            if (hostLastSeen >= claimCutoff)
                return ServiceResult<MultiplayerSessionDto>.Failure(
                    ApiErrors.HostStillActive,
                    ServiceErrorKind.Conflict,
                    "The current host is still within its grace period.",
                    new Dictionary<string, object?>
                    {
                        ["hostLastSeenAtUtc"] = hostLastSeen,
                        ["hostClaimGraceSeconds"] = _options.HostClaimGraceSeconds
                    });
        }

        // Already there. Not an error — a client retrying an unacknowledged claim asked for a state
        // that now holds.
        if (session.HostUserId == request.ToUserId)
            return ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken));

        session.HostUserId = request.ToUserId;

        // Both flags move inside the one transaction, so the roster and the session can never
        // disagree about who is in charge.
        foreach (var player in seated)
            player.IsHost = player.UserId == request.ToUserId;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // **Exactly one simultaneous claim commits.** RowVersion is what decides it. The loser
            // re-reads and reports what actually happened rather than retrying into a second
            // migration — two clients ping-ponging authority is far worse than one losing a race.
            Detach();

            var winner = await _dbContext.MultiplayerSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

            if (winner is null)
                return SessionNotFound<MultiplayerSessionDto>(sessionId);

            if (winner.HostUserId == request.ToUserId)
                return ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken));

            return ServiceResult<MultiplayerSessionDto>.Failure(
                ApiErrors.HostStillActive,
                ServiceErrorKind.Conflict,
                "Another claim reached the session first.",
                new Dictionary<string, object?> { ["hostUserId"] = winner.HostUserId });
        }

        var dto = await BuildAsync(sessionId, cancellationToken);

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.HostTransfer, sessionId, dto, StatusCodes.Ok, cancellationToken);

        return ServiceResult<MultiplayerSessionDto>.Success(dto);
    }

    // ---- reads ---------------------------------------------------------------------------------

    public async Task<ServiceResult<MultiplayerSessionDto>> GetAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<MultiplayerSessionDto>(sessionId);

        return ServiceResult<MultiplayerSessionDto>.Success(await BuildAsync(sessionId, cancellationToken));
    }

    public async Task<ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>> GetPlayersAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsMemberAsync(userId, sessionId, cancellationToken))
            return SessionNotFound<IReadOnlyList<MultiplayerSessionPlayerDto>>(sessionId);

        var players = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .OrderBy(p => p.Slot)
            .ToListAsync(cancellationToken);

        var names = await ResolveDisplayNamesAsync(players.Select(p => p.UserId), cancellationToken);

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

        var names = await ResolveDisplayNamesAsync(
            rows.SelectMany(s => s.Players).Select(p => p.UserId), cancellationToken);

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

        var names = await ResolveDisplayNamesAsync(session.Players.Select(p => p.UserId), cancellationToken);

        return session.ToDto(names, DateTime.UtcNow);
    }

    /// <summary>
    /// Display names for a roster, in one query. Accounts that never completed a profile are simply
    /// absent, which the DTO renders as null rather than as an empty string the client would have to
    /// special-case.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveDisplayNamesAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId) && p.FullName != string.Empty)
            .ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);
    }

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
    /// </summary>
    private Task<bool> IsMemberAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .AnyAsync(p => p.SessionId == sessionId && p.UserId == userId, cancellationToken);

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
    /// </summary>
    private static string GenerateJoinCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        return string.Create(6, alphabet, (span, source) =>
        {
            for (var index = 0; index < span.Length; index++)
                span[index] = source[Random.Shared.Next(source.Length)];
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

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

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

    private static ServiceResult<T> InvalidTransition<T>(MultiplayerSessionState? from) =>
        ServiceResult<T>.Failure(
            ApiErrors.SessionInvalidTransition,
            ServiceErrorKind.Conflict,
            from is null
                ? "The session moved while this request was in flight. Re-read it and re-evaluate."
                : $"The requested move is not legal from {from}.");

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
