using Microsoft.EntityFrameworkCore;
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
/// Find a session, or start one. See <see cref="IMatchmakingService"/> for why there is no queue.
/// </summary>
public class MatchmakingService : IMatchmakingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerSessionService _sessions;
    private readonly MultiplayerRequestLogStore _log;
    private readonly ISessionLessonMatcher _lessons;
    private readonly IPlaySelectionResolver _play;
    private readonly ILanguageService _languageService;
    private readonly MultiplayerOptions _options;

    public MatchmakingService(
        ApplicationDbContext dbContext,
        MultiplayerSessionService sessions,
        MultiplayerRequestLogStore log,
        ISessionLessonMatcher lessons,
        IPlaySelectionResolver play,
        ILanguageService languageService,
        IOptions<MultiplayerOptions> options)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _log = log;
        _lessons = lessons;
        _play = play;
        _languageService = languageService;
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

        // The axes this search is scoped by, checked once: an unknown or withdrawn mode, a closed
        // event or a grade-gated one is refused here rather than after a room has been created.
        var selection = await _play.ResolveAsync(
            userId,
            new PlaySelectionRequest
            {
                GameId = request.GameId,
                ModeKey = request.ModeKey,
                ContextKey = request.EventId is null ? null : PlayContextTokens.Event,
                EventId = request.EventId,

                // Two, because matchmaking is by definition looking for company: a mode offered only
                // solo has nothing to match into.
                PlayerCount = 2
            },
            cancellationToken);

        if (!selection.Succeeded)
            return new ServiceResult<MatchmakeResponse>
            {
                ErrorKind = selection.ErrorKind,
                Errors = selection.Errors,
                Error = selection.Error,
                Details = selection.Details
            };

        var play = selection.Value!;
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        // Subject-scoped matchmaking: the caller named a subject and no lesson, so the server works
        // out what they can play and looks for somebody they overlap with. This is the whole reason a
        // child can now match at all — requiring both players to have chosen the same lesson meant
        // matching only with somebody at exactly the same point in the curriculum.
        IReadOnlyList<Guid> myLessons = [];

        if (request.CurriculumPath?.LessonId is null && request.CurriculumPath?.SubjectId is { } subjectId)
        {
            var eligible = await _lessons.EligibleLessonsAsync(
                userId, request.GameId, subjectId, langId, cancellationToken);

            if (eligible.Count == 0)
                return ServiceResult<MatchmakeResponse>.Failure(
                    ApiErrors.PlayNoSharedLesson,
                    ServiceErrorKind.Conflict,
                    "You have no unlocked lessons with questions in this subject yet.");

            myLessons = eligible.Select(l => l.LessonId).ToList();
        }

        foreach (var candidateId in await FindCandidatesAsync(request, play, langId, myLessons, cancellationToken))
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
            ModeKey = request.ModeKey,
            EventId = request.EventId,

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
    /// <summary>
    /// The sessions this caller could join, best first.
    /// <para>
    /// Two shapes, and the difference is what the caller asked for. An exact lesson matches only
    /// sessions playing that lesson — unchanged behaviour, and still right for a direct invite. A
    /// subject matches sessions whose players share a lesson with this one: either the session has
    /// already stamped a lesson this caller can play, or it has stamped none and its remaining
    /// candidates overlap theirs.
    /// </para>
    /// <para>
    /// Mode and event are filtered either way. Matching a child into a session playing different
    /// rules would be worse than not matching them at all.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<Guid>> FindCandidatesAsync(
        MatchmakeRequest request,
        PlaySelection play,
        Guid langId,
        IReadOnlyList<Guid> myLessons,
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
                        && s.LastHeartbeatAtUtc > freshCutoff
                        && s.ModeId == play.ModeId
                        && s.EventId == play.EventId);

        if (request.CurriculumPath?.LessonId is { } lessonId)
        {
            // Reads the real column rather than the JSON blob, precisely so this stays an index seek.
            query = query.Where(s => s.LessonId == lessonId);
        }
        else if (request.CurriculumPath?.SubjectId is { } subjectId && myLessons.Count > 0)
        {
            query = query.Where(s =>
                s.SubjectId == subjectId
                && s.LangId == langId
                && (s.LessonId == null
                    ? s.EligibleLessons.Any(l => myLessons.Contains(l.LessonId))
                    : myLessons.Contains(s.LessonId.Value)));
        }

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
