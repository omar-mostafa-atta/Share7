using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Application.Progression.Interfaces;
using Share7.Domain.Leaderboards;
using Share7.Domain.Organizations;
using Share7.Domain.Play;
using Share7.Domain.Runs;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// The one gate every session passes through.
/// <para>
/// <b>Order matters here, and it is cheapest-first for a reason.</b> The common call is a curriculum
/// run of a default mode by a child who is allowed to play it, and that path must cost one indexed
/// read. The profile, the grade and the entry counts are only loaded when something actually asks
/// for them, so an ordinary lesson never pays for the event machinery.
/// </para>
/// </summary>
public class PlaySelectionResolver : IPlaySelectionResolver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILevelService _levels;

    public PlaySelectionResolver(ApplicationDbContext dbContext, ILevelService levels)
    {
        _dbContext = dbContext;
        _levels = levels;
    }

    public async Task<ServiceResult<PlaySelection>> ResolveAsync(
        Guid userId, PlaySelectionRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // ---- context ---------------------------------------------------------------------
        if (!PlayContextTokens.TryParse(request.ContextKey ?? PlayContextTokens.Curriculum, out var context))
            return Refuse(
                ApiErrors.PlayContextInvalid,
                ServiceErrorKind.Validation,
                $"'{request.ContextKey}' is not a context this server knows.");

        if (context == PlayContextKind.Assignment && request.AssignmentId is null)
            return Refuse(
                ApiErrors.PlayContextInvalid,
                ServiceErrorKind.Validation,
                "An assignment context must name the assignment it is being played for.");

        if (context != PlayContextKind.Assignment && request.AssignmentId is not null)
            return Refuse(
                ApiErrors.PlayContextInvalid,
                ServiceErrorKind.Validation,
                "An assignment id was sent with a context that is not an assignment. Nothing would " +
                "have been credited to that assignment, so the selection is refused rather than " +
                "silently ignored.");

        if (context == PlayContextKind.Event && request.EventId is null)
            return Refuse(
                ApiErrors.PlayContextInvalid,
                ServiceErrorKind.Validation,
                "An event context must name the event it is being played in.");

        if (context != PlayContextKind.Event && request.EventId is not null)
            return Refuse(
                ApiErrors.PlayContextInvalid,
                ServiceErrorKind.Validation,
                "An event id was sent with a context that is not an event. Nothing would have been " +
                "ranked in that event, so the selection is refused rather than silently ignored.");

        // ---- mode ------------------------------------------------------------------------
        var modeKey = (request.ModeKey ?? string.Empty).Trim();

        var mode = modeKey.Length > 0
            ? await _dbContext.GameModes
                .AsNoTracking()
                .Include(m => m.EntitlementProduct)
                .FirstOrDefaultAsync(m => m.ModeKey == modeKey, cancellationToken)
            : await _dbContext.GameModes
                .AsNoTracking()
                .Include(m => m.EntitlementProduct)
                .FirstOrDefaultAsync(m => m.GameId == request.GameId && m.IsDefault, cancellationToken);

        if (modeKey.Length > 0 && mode is null)
            return Refuse(
                ApiErrors.PlayModeUnknown,
                ServiceErrorKind.Validation,
                $"'{modeKey}' names no mode.");

        if (mode is not null)
        {
            if (mode.GameId != request.GameId)
                return Refuse(
                    ApiErrors.PlayModeWrongGame,
                    ServiceErrorKind.Validation,
                    $"Mode '{mode.ModeKey}' belongs to another game.");

            if (!mode.IsOffered(now))
                return Refuse(
                    ApiErrors.PlayModeInactive,
                    ServiceErrorKind.Forbidden,
                    $"Mode '{mode.ModeKey}' is not being offered right now.",
                    new Dictionary<string, object?>
                    {
                        ["availableFromUtc"] = mode.AvailableFromUtc,
                        ["availableToUtc"] = mode.AvailableToUtc
                    });

            if (request.PlayerCount > 0
                && !PlayTopologyTokens.AllowsPlayerCount(mode.Topologies, request.PlayerCount))
                return Refuse(
                    ApiErrors.PlayTopologyMismatch,
                    ServiceErrorKind.Validation,
                    $"Mode '{mode.ModeKey}' is not played with {request.PlayerCount} player(s).",
                    new Dictionary<string, object?>
                    {
                        ["topologies"] = PlayTopologyTokens.ToTokens(mode.Topologies),
                        ["minPlayers"] = mode.MinPlayers,
                        ["maxPlayers"] = mode.MaxPlayers
                    });

            if (mode.RequiresEntitlement)
            {
                if (mode.EntitlementProductId is not { } productId)
                    return Refuse(
                        ApiErrors.PlayModeNotEntitled,
                        ServiceErrorKind.Forbidden,
                        $"Mode '{mode.ModeKey}' is sold but names no product, so nobody can own it.");

                var owns = await _dbContext.Entitlements
                    .AsNoTracking()
                    .AnyAsync(e => e.UserId == userId && e.ProductId == productId, cancellationToken);

                if (!owns)
                    return Refuse(
                        ApiErrors.PlayModeNotEntitled,
                        ServiceErrorKind.Forbidden,
                        $"Mode '{mode.ModeKey}' has not been unlocked on this account.",
                        new Dictionary<string, object?>
                        {
                            ["entitlementSku"] = mode.EntitlementProduct?.Key
                        });
            }

            if (mode.MinGradeOrder > 0)
            {
                var gradeOrder = await GradeOrderAsync(userId, cancellationToken);

                if (gradeOrder < mode.MinGradeOrder)
                    return Refuse(
                        ApiErrors.PlayModeGradeGated,
                        ServiceErrorKind.Forbidden,
                        $"Mode '{mode.ModeKey}' opens at grade order {mode.MinGradeOrder}.",
                        new Dictionary<string, object?>
                        {
                            ["minGradeOrder"] = mode.MinGradeOrder,
                            ["gradeOrder"] = gradeOrder
                        });
            }
        }

        // ---- event -----------------------------------------------------------------------
        PlayEvent? playEvent = null;

        if (context == PlayContextKind.Event)
        {
            var eventResult = await ResolveEventAsync(userId, request, mode, now, cancellationToken);

            if (!eventResult.Succeeded)
                return Propagate(eventResult);

            playEvent = eventResult.Value!;
        }

        // ---- assignment ------------------------------------------------------------------
        Assignment? assignment = null;

        if (context == PlayContextKind.Assignment)
        {
            var assignmentResult = await ResolveAssignmentAsync(userId, request, cancellationToken);

            if (!assignmentResult.Succeeded)
                return Propagate(assignmentResult);

            assignment = assignmentResult.Value!;
        }

        // ---- what it is worth -------------------------------------------------------------
        var policy = mode is null
            // No modes authored for this game: the platform behaves exactly as it did before modes
            // existed, and the context alone decides. Practice still pays nothing — that rule is the
            // context's, not the mode's.
            ? LegacyPolicyFor(context)
            : PlayAccounting.Decide(mode, context);

        var profile = await ResolveProfileAsync(playEvent, mode, cancellationToken);

        return ServiceResult<PlaySelection>.Success(new PlaySelection
        {
            Mode = mode,
            Context = context,
            Event = playEvent,
            Assignment = assignment,
            Policy = policy,
            Profile = profile
        });
    }

    public async Task<PlaySelection> DescribeAsync(
        Guid? modeId,
        PlayContextKind context,
        Guid? eventId,
        CancellationToken cancellationToken = default)
    {
        var mode = modeId is { } id
            ? await _dbContext.GameModes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            : null;

        var playEvent = eventId is { } eid
            ? await _dbContext.PlayEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eid, cancellationToken)
            : null;

        return new PlaySelection
        {
            Mode = mode,
            Context = context,
            Event = playEvent,
            Policy = mode is null ? LegacyPolicyFor(context) : PlayAccounting.Decide(mode, context),
            Profile = await ResolveProfileAsync(playEvent, mode, cancellationToken)
        };
    }

    /// <summary>
    /// What a session is worth on a database whose modes were never authored — today's behaviour,
    /// unchanged, with practice's one rule applied.
    /// </summary>
    private static PlaySettlementPolicy LegacyPolicyFor(PlayContextKind context) => context switch
    {
        PlayContextKind.Practice => PlaySettlementPolicy.Nothing,

        // An assignment is curriculum work with a teacher's name on it. It settles as curriculum
        // does, because withholding stars and coins for the work a school set would teach a child
        // that school work is the unrewarding kind.
        PlayContextKind.Assignment => new PlaySettlementPolicy(true, true, true),
        PlayContextKind.Curriculum => new PlaySettlementPolicy(true, true, true),
        _ => new PlaySettlementPolicy(false, true, true)
    };

    /// <summary>
    /// The assignment this session claims to be fulfilling, if the caller may actually play it.
    ///
    /// <para><b>Three checks, and the roster one is the point.</b> The assignment must exist and be
    /// open; the caller must be a current learner in the cohort it was set for; and when the
    /// assignment names a lesson, the session must be playing that lesson. Without the second, any
    /// client could credit its work to any class — which is precisely the kind of claim the play
    /// boundary exists to refuse (§17.4).</para>
    ///
    /// <para><b>What it deliberately does not do is change what the session is worth.</b> A child
    /// doing homework their teacher set is doing their own curriculum work, and withholding stars
    /// and coins for it would teach them that school work is the unrewarding kind. The honesty
    /// lives in the evidence contract, where an assignment context is admitted or not and its
    /// strength follows the conditions actually recorded — not in the payout.</para>
    /// </summary>
    private async Task<ServiceResult<Assignment>> ResolveAssignmentAsync(
        Guid userId,
        PlaySelectionRequest request,
        CancellationToken cancellationToken)
    {
        var assignment = await _dbContext.Assignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId, cancellationToken);

        if (assignment is null || assignment.WithdrawnAtUtc is not null)
            return ServiceResult<Assignment>.Failure(
                ApiErrors.PlayContextInvalid,
                ServiceErrorKind.NotFound,
                "No open assignment with that id.");

        var onRoster = await _dbContext.CohortMemberships
            .AsNoTracking()
            .AnyAsync(cm => cm.CohortId == assignment.CohortId
                            && cm.UserId == userId
                            && cm.Role == CohortRole.Learner
                            && cm.LeftAtUtc == null,
                cancellationToken);

        if (!onRoster)
            return ServiceResult<Assignment>.Failure(
                ApiErrors.PlayContextInvalid,
                ServiceErrorKind.Validation,
                "That assignment was set for a cohort you are not in.");

        return ServiceResult<Assignment>.Success(assignment);
    }

    private async Task<ServiceResult<PlayEvent>> ResolveEventAsync(
        Guid userId,
        PlaySelectionRequest request,
        GameMode? mode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var playEvent = await _dbContext.PlayEvents
            .AsNoTracking()
            .Include(e => e.Cycle)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken);

        if (playEvent is null || !playEvent.IsActive || playEvent.CancelledAtUtc is not null)
            return ServiceResult<PlayEvent>.Failure(
                ApiErrors.PlayEventUnknown,
                ServiceErrorKind.NotFound,
                "No event with that id is running.");

        if (playEvent.GameId != request.GameId)
            return ServiceResult<PlayEvent>.Failure(
                ApiErrors.PlayEventUnknown,
                ServiceErrorKind.NotFound,
                "That event belongs to another game.");

        if (mode is not null && playEvent.ModeId != mode.Id)
            return ServiceResult<PlayEvent>.Failure(
                ApiErrors.PlayEventModeMismatch,
                ServiceErrorKind.Validation,
                "This event is played in a different mode.",
                new Dictionary<string, object?> { ["eventModeId"] = playEvent.ModeId });

        var cycleState = playEvent.Cycle?.State ?? LeaderboardCycleState.Scheduled;

        // The cycle owns the window, so "is it open" is answered by the ladder rather than by
        // comparing the client's clock — or this row's, which deliberately has no dates on it.
        if (!playEvent.AcceptsEntries(cycleState))
            return ServiceResult<PlayEvent>.Failure(
                ApiErrors.PlayEventClosed,
                ServiceErrorKind.Forbidden,
                "This event is not accepting entries.",
                new Dictionary<string, object?>
                {
                    ["state"] = WireEnum.ToWire(cycleState),
                    ["startsAtUtc"] = playEvent.Cycle?.StartsAtUtc,
                    ["endsAtUtc"] = playEvent.Cycle?.EndsAtUtc
                });

        if (playEvent.MinGradeOrder > 0 || playEvent.MaxGradeOrder > 0)
        {
            var gradeOrder = await GradeOrderAsync(userId, cancellationToken);

            if (playEvent.MinGradeOrder > 0 && gradeOrder < playEvent.MinGradeOrder
                || playEvent.MaxGradeOrder > 0 && gradeOrder > playEvent.MaxGradeOrder)
                return NotEligible(
                    "This event is for a different school year.",
                    new Dictionary<string, object?>
                    {
                        ["minGradeOrder"] = playEvent.MinGradeOrder,
                        ["maxGradeOrder"] = playEvent.MaxGradeOrder
                    });
        }

        if (playEvent.MinLevel > 0)
        {
            var level = await _levels.GetForUserAsync(userId, cancellationToken);

            if (level.Level < playEvent.MinLevel)
                return NotEligible(
                    $"This event opens at level {playEvent.MinLevel}.",
                    new Dictionary<string, object?>
                    {
                        ["minLevel"] = playEvent.MinLevel,
                        ["level"] = level.Level
                    });
        }

        if (playEvent.EntryProductId is { } entryProductId)
        {
            var owns = await _dbContext.Entitlements
                .AsNoTracking()
                .AnyAsync(e => e.UserId == userId && e.ProductId == entryProductId, cancellationToken);

            if (!owns)
                return NotEligible("This event is open to members only.", null);
        }

        // Entry limits count **settled** runs, not started ones. A run that was opened and abandoned
        // cost the child nothing and must not burn one of their entries — the opposite rule would
        // punish a dropped connection.
        if (playEvent.MaxEntriesPerDay is { } perDay)
        {
            var today = await _dbContext.Runs
                .AsNoTracking()
                .CountAsync(
                    r => r.UserId == userId
                         && r.EventId == playEvent.Id
                         && r.State == RunState.Settled
                         && r.EndedAtUtc >= now.Date,
                    cancellationToken);

            if (today >= perDay)
                return ServiceResult<PlayEvent>.Failure(
                    ApiErrors.PlayEventEntryLimit,
                    ServiceErrorKind.Forbidden,
                    "You have used all of today's entries for this event.",
                    new Dictionary<string, object?>
                    {
                        ["maxEntriesPerDay"] = perDay,
                        ["entriesToday"] = today,
                        ["resetsAtUtc"] = now.Date.AddDays(1)
                    });
        }

        if (playEvent.MaxEntriesTotal is { } total)
        {
            var all = await _dbContext.Runs
                .AsNoTracking()
                .CountAsync(
                    r => r.UserId == userId && r.EventId == playEvent.Id && r.State == RunState.Settled,
                    cancellationToken);

            if (all >= total)
                return ServiceResult<PlayEvent>.Failure(
                    ApiErrors.PlayEventEntryLimit,
                    ServiceErrorKind.Forbidden,
                    "You have used all of your entries for this event.",
                    new Dictionary<string, object?>
                    {
                        ["maxEntriesTotal"] = total,
                        ["entries"] = all
                    });
        }

        return ServiceResult<PlayEvent>.Success(playEvent);
    }

    /// <summary>
    /// The profile a session settles under: the event's, then the mode's, then the platform default.
    /// <para>
    /// The event wins over the mode deliberately. An operator running a competition is choosing what
    /// that competition pays, and a mode's ordinary profile would otherwise silently override the
    /// event they authored.
    /// </para>
    /// </summary>
    private async Task<EconomyProfile?> ResolveProfileAsync(
        PlayEvent? playEvent, GameMode? mode, CancellationToken cancellationToken)
    {
        var profileId = playEvent?.EconomyProfileId ?? mode?.EconomyProfileId;

        if (profileId is { } id)
            return await _dbContext.EconomyProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return await _dbContext.EconomyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsDefault, cancellationToken);
    }

    /// <summary>
    /// The player's grade order, or 0 when they have no profile yet. Zero passes every gate, which is
    /// the right default: a child who has not chosen a grade is not who a grade gate is aimed at.
    /// </summary>
    private async Task<int> GradeOrderAsync(Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Grade!.Order)
            .FirstOrDefaultAsync(cancellationToken);

    private static ServiceResult<PlayEvent> NotEligible(
        string message, IReadOnlyDictionary<string, object?>? details) =>
        ServiceResult<PlayEvent>.Failure(
            ApiErrors.PlayEventNotEligible, ServiceErrorKind.Forbidden, message, details);

    private static ServiceResult<PlaySelection> Refuse(
        ApiErrorCode error,
        ServiceErrorKind kind,
        string message,
        IReadOnlyDictionary<string, object?>? details = null) =>
        ServiceResult<PlaySelection>.Failure(error, kind, message, details);

    private static ServiceResult<PlaySelection> Propagate(ServiceResult source) => new()
    {
        ErrorKind = source.ErrorKind,
        Errors = source.Errors,
        Error = source.Error,
        Details = source.Details
    };
}
