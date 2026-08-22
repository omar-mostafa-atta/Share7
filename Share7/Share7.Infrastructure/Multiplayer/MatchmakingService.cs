using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Find a session, or start one. See <see cref="IMatchmakingService"/> for why there is no queue.
/// </summary>
public class MatchmakingService : IMatchmakingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerSessionService _sessions;
    private readonly MultiplayerRequestLogStore _log;
    private readonly MultiplayerOptions _options;

    public MatchmakingService(
        ApplicationDbContext dbContext,
        MultiplayerSessionService sessions,
        MultiplayerRequestLogStore log,
        IOptions<MultiplayerOptions> options)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _log = log;
        _options = options.Value;
    }

    public async Task<ServiceResult<MatchmakeResponse>> MatchmakeAsync(
        Guid userId,
        MatchmakeRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = MultiplayerRequestLogStore.ResolveKey(request.RequestId);

        // **Replay matters more here than anywhere else.** Nothing else in the schema still records
        // whether a session was joined or created once it exists, so without the stored body a retry
        // could not be told which of the two happened the first time.
        if (await _log.TryReplayAsync<MatchmakeResponse>(
                userId, requestId, MultiplayerOperations.Matchmake, cancellationToken) is { } replayed)
            return ServiceResult<MatchmakeResponse>.Success(replayed);

        if (!_options.EffectiveProtocolVersions.Contains(request.ProtocolVersion))
            return ServiceResult<MatchmakeResponse>.Failure(
                ApiErrors.ProtocolVersionMismatch,
                ServiceErrorKind.Validation,
                $"Protocol version {request.ProtocolVersion} is not accepted by this server.",
                new Dictionary<string, object?>
                {
                    ["requested"] = request.ProtocolVersion,
                    ["accepted"] = _options.EffectiveProtocolVersions
                });

        if (await _sessions.HasActiveMembershipAsync(userId, cancellationToken))
            return ServiceResult<MatchmakeResponse>.Failure(
                ApiErrors.AlreadyInSession,
                ServiceErrorKind.Conflict,
                "The caller already holds a seat in a session that has not ended.");

        foreach (var candidateId in await FindCandidatesAsync(request, cancellationToken))
        {
            var seated = await _sessions.SeatAsync(userId, candidateId, request.ProtocolVersion, cancellationToken);

            if (seated.Succeeded)
                return await CompleteAsync(
                    userId, requestId, MatchOutcome.Joined, seated.Value!, cancellationToken);

            // **A refusal about the caller ends the search; a refusal about the candidate does not.**
            // A session that filled up between selection and seating is exactly the race this loop
            // exists to absorb — move on. But if the caller has picked up a membership in the
            // meantime, every remaining candidate would refuse them for the same reason.
            if (seated.Error?.Code == ApiErrors.AlreadyInSession.Code)
                return ServiceResult<MatchmakeResponse>.Failure(
                    ApiErrors.AlreadyInSession,
                    ServiceErrorKind.Conflict,
                    "The caller already holds a seat in a session that has not ended.");
        }

        if (!request.CreateIfNoneFound)
            return ServiceResult<MatchmakeResponse>.Success(new MatchmakeResponse
            {
                Outcome = MatchOutcome.NoMatch
            });

        var transportName = (request.TransportSessionName ?? string.Empty).Trim();

        // Only checked once creating is actually on the table. A caller that matched into an
        // existing session never needed to supply one.
        if (transportName.Length == 0)
            return ServiceResult<MatchmakeResponse>.Failure(
                ApiErrors.ValidationFailed,
                ServiceErrorKind.Validation,
                "transportSessionName is required when createIfNoneFound is true.");

        var created = await _sessions.CreateAsync(userId, new CreateMultiplayerSessionRequest
        {
            GameId = request.GameId,
            TransportSessionName = transportName,
            TransportRegion = request.TransportRegion,
            Visibility = SessionVisibility.Public,
            MaxPlayers = request.MaxPlayers,
            IsRanked = request.IsRanked,
            ProtocolVersion = request.ProtocolVersion,
            CurriculumPath = request.CurriculumPath,

            // **No request id passed through.** The create has to be idempotent under *this* call's
            // key, not under its own — and the matchmake log entry written below is what protects
            // the retry. Forwarding the key would spend it on the wrong operation and make the
            // matchmake replay lookup miss.
            RequestId = null
        }, cancellationToken);

        if (!created.Succeeded)
            return ServiceResult<MatchmakeResponse>.Failure(
                created.Error ?? ApiErrors.ValidationFailed,
                created.ErrorKind,
                string.Join(" ", created.Errors),
                created.Details);

        return await CompleteAsync(userId, requestId, MatchOutcome.Created, created.Value!, cancellationToken);
    }

    /// <summary>
    /// Sessions worth trying, best first.
    /// <para>
    /// **Fullest first.** A session with three of four seats taken is one join away from starting,
    /// so filling it is the shortest wait for everybody in it — spreading players evenly across
    /// half-empty rooms is how nobody's match ever begins. Oldest first breaks the tie, so a session
    /// cannot be passed over indefinitely.
    /// </para>
    /// <para>
    /// Stale sessions are never offered: a host that stopped heartbeating is about to be swept, and
    /// seating somebody into it would put them in a room that is already dying.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<Guid>> FindCandidatesAsync(
        MatchmakeRequest request,
        CancellationToken cancellationToken)
    {
        var freshCutoff = DateTime.UtcNow.AddSeconds(-_options.SessionTimeoutSeconds);

        var query = _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.GameId == request.GameId
                        && s.State == MultiplayerSessionState.Created
                        && s.Visibility == SessionVisibility.Public
                        && s.IsRanked == request.IsRanked
                        && s.ProtocolVersion == request.ProtocolVersion
                        && s.CurrentPlayerCount < s.MaxPlayers
                        && s.LastHeartbeatAtUtc > freshCutoff);

        // The one curriculum filter for v1, agreed with the Unity dev. It reads a real column rather
        // than the JSON blob precisely so this stays an index seek.
        if (request.CurriculumPath?.LessonId is { } lessonId)
            query = query.Where(s => s.LessonId == lessonId);

        return await query
            .OrderByDescending(s => s.CurrentPlayerCount)
            .ThenBy(s => s.CreatedAtUtc)
            .Take(Math.Max(1, _options.MatchmakingCandidateLimit))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<ServiceResult<MatchmakeResponse>> CompleteAsync(
        Guid userId,
        string requestId,
        MatchOutcome outcome,
        MultiplayerSessionDto session,
        CancellationToken cancellationToken)
    {
        var response = new MatchmakeResponse { Outcome = outcome, Session = session };

        await _log.RecordAsync(
            userId, requestId, MultiplayerOperations.Matchmake, session.Id, response, 200, cancellationToken);

        return ServiceResult<MatchmakeResponse>.Success(response);
    }
}
