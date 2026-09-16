using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Application.Progression.Interfaces;
using Share7.Domain.Leaderboards;
using Share7.Domain.Play;
using Share7.Domain.Runs;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Events as an entrant reads them.
/// <para>
/// <b>Everything time-dependent is answered by the server.</b> Whether an event is open, how many
/// entries are left today, where the caller stands — a device clock decides none of it, and the
/// response carries <c>serverTimeUtc</c> so a countdown can be drawn against the right one.
/// </para>
/// <para>
/// A settled event stays visible for a while rather than vanishing the moment it ends: the results
/// are the part an entrant most wants to see, and an event that disappears at the final whistle
/// takes its own outcome with it.
/// </para>
/// </summary>
public class PlayEventService : IPlayEventService
{
    /// <summary>How long a finished event stays in the listing so its winners can be read.</summary>
    private static readonly TimeSpan ResultsWindow = TimeSpan.FromDays(7);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;
    private readonly ILevelService _levels;

    public PlayEventService(
        ApplicationDbContext dbContext, ILanguageService languageService, ILevelService levels)
    {
        _dbContext = dbContext;
        _languageService = languageService;
        _levels = levels;
    }

    public async Task<PlayEventsResponse> ListAsync(
        Guid userId,
        string? gameKey = null,
        bool includeScheduled = true,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var key = (gameKey ?? string.Empty).Trim();

        var query = BaseQuery()
            .Where(e => e.IsActive)
            .Where(e => e.Cycle!.EndsAtUtc > now - ResultsWindow);

        if (key.Length > 0)
            query = query.Where(e => e.Game!.GameKey == key);

        if (!includeScheduled)
            query = query.Where(e => e.Cycle!.State != LeaderboardCycleState.Scheduled);

        var events = await query
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Cycle!.StartsAtUtc)
            .ToListAsync(cancellationToken);

        return new PlayEventsResponse
        {
            ServerTimeUtc = now,
            Events = await DescribeAsync(userId, events, now, cancellationToken)
        };
    }

    public async Task<ServiceResult<PlayEventDto>> GetAsync(
        Guid userId, Guid eventId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var playEvent = await BaseQuery().FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (playEvent is null || !playEvent.IsActive)
            return ServiceResult<PlayEventDto>.Failure(
                ApiErrors.PlayEventUnknown, ServiceErrorKind.NotFound, "No event with that id.");

        var described = await DescribeAsync(userId, [playEvent], now, cancellationToken);

        return ServiceResult<PlayEventDto>.Success(described[0]);
    }

    public async Task<IReadOnlyList<EventAwardDto>> GetAwardsAsync(
        Guid userId, bool unseenOnly = false, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var query = _dbContext.EventAwards
            .AsNoTracking()
            .Include(a => a.Event).ThenInclude(e => e!.Translations)
            .Include(a => a.Tier).ThenInclude(t => t!.Translations)
            .Include(a => a.Claim)
            .Where(a => a.UserId == userId);

        if (unseenOnly)
            query = query.Where(a => a.SeenAtUtc == null);

        var awards = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return awards.Select(a => Map(a, langId)).ToList();
    }

    public async Task<ServiceResult> MarkAwardSeenAsync(
        Guid userId, Guid awardId, CancellationToken cancellationToken = default)
    {
        var award = await _dbContext.EventAwards
            .FirstOrDefaultAsync(a => a.Id == awardId && a.UserId == userId, cancellationToken);

        if (award is null)
            return ServiceResult.Failure(
                ApiErrors.NotFound, ServiceErrorKind.NotFound, "No award with that id belongs to this account.");

        // Set once. A retry must not move the moment the child first saw it, because that timestamp
        // is what decides whether the app celebrates the prize again.
        if (award.SeenAtUtc is null)
        {
            award.SeenAtUtc = DateTime.UtcNow;
            award.UpdatedAtUtc = award.SeenAtUtc;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------- describing

    private IQueryable<PlayEvent> BaseQuery() =>
        _dbContext.PlayEvents
            .AsNoTracking()
            .Include(e => e.Translations)
            .Include(e => e.Game)
            .Include(e => e.Mode)
            .Include(e => e.Board)
            .Include(e => e.Cycle)
            .Include(e => e.EntryProduct)
            .Include(e => e.PrizeTiers).ThenInclude(t => t.Translations);

    /// <summary>
    /// Fills in everything that depends on who is asking: eligibility, entries used, and standing.
    /// <para>
    /// Batched deliberately. The home screen asks for every live event at once, and a per-event
    /// eligibility query would turn one card into a dozen round trips on a shared host.
    /// </para>
    /// </summary>
    private async Task<List<PlayEventDto>> DescribeAsync(
        Guid userId, List<PlayEvent> events, DateTime now, CancellationToken cancellationToken)
    {
        if (events.Count == 0) return [];

        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var eventIds = events.Select(e => e.Id).ToList();

        var needsGrade = events.Any(e => e.MinGradeOrder > 0 || e.MaxGradeOrder > 0
                                         || e.PrizeCohort == LeaderboardCohort.Grade);
        var needsLevel = events.Any(e => e.MinLevel > 0);

        var profile = needsGrade
            ? await _dbContext.StudentProfiles
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => new { p.GradeId, Order = p.Grade!.Order })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var gradeOrder = profile?.Order ?? 0;
        var level = needsLevel ? (await _levels.GetForUserAsync(userId, cancellationToken)).Level : 0;

        var entryProductIds = events
            .Where(e => e.EntryProductId is not null)
            .Select(e => e.EntryProductId!.Value)
            .Distinct()
            .ToList();

        var ownedProducts = entryProductIds.Count == 0
            ? new HashSet<Guid>()
            : (await _dbContext.Entitlements
                .AsNoTracking()
                .Where(e => e.UserId == userId && entryProductIds.Contains(e.ProductId))
                .Select(e => e.ProductId)
                .ToListAsync(cancellationToken))
                .ToHashSet();

        // Entries are settled runs, counted per event: one grouped read for every event on the page.
        var entries = await _dbContext.Runs
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && r.EventId != null
                        && eventIds.Contains(r.EventId.Value)
                        && r.State == RunState.Settled)
            .GroupBy(r => r.EventId!.Value)
            .Select(g => new
            {
                EventId = g.Key,
                Total = g.Count(),
                Today = g.Count(r => r.EndedAtUtc >= now.Date)
            })
            .ToDictionaryAsync(x => x.EventId, x => x, cancellationToken);

        var cycleIds = events.Select(e => e.CycleId).ToList();

        var standings = await _dbContext.LeaderboardEntries
            .AsNoTracking()
            .Where(e => cycleIds.Contains(e.CycleId) && e.UserId == userId)
            .Select(e => new { e.CycleId, e.Cohort, e.CohortKey, e.Rank, e.Value })
            .ToListAsync(cancellationToken);

        var result = new List<PlayEventDto>(events.Count);

        foreach (var playEvent in events)
        {
            var translation = playEvent.Translations.FirstOrDefault(t => t.LangId == langId);
            var cycleState = playEvent.Cycle?.State ?? LeaderboardCycleState.Scheduled;
            var used = entries.GetValueOrDefault(playEvent.Id);

            var cohortKey = playEvent.PrizeCohort == LeaderboardCohort.Grade
                ? profile?.GradeId ?? Guid.Empty
                : Guid.Empty;

            var standing = standings.FirstOrDefault(s =>
                s.CycleId == playEvent.CycleId
                && s.Cohort == playEvent.PrizeCohort
                && s.CohortKey == cohortKey);

            var ineligible = IneligibleCodeFor(
                playEvent, cycleState, gradeOrder, level, ownedProducts,
                used?.Today ?? 0, used?.Total ?? 0);

            result.Add(new PlayEventDto
            {
                EventId = playEvent.Id,
                EventKey = playEvent.EventKey,
                Name = translation?.Name ?? string.Empty,
                Description = translation?.Description ?? string.Empty,
                Rules = translation?.Rules ?? string.Empty,
                GameKey = playEvent.Game?.GameKey ?? string.Empty,
                ModeKey = playEvent.Mode?.ModeKey ?? string.Empty,
                WorldKey = playEvent.WorldKey,
                GrantsWorldForDuration = playEvent.GrantsWorldForDuration,
                BoardKey = playEvent.Board?.BoardKey ?? string.Empty,
                BoardId = playEvent.BoardId,
                CycleId = playEvent.CycleId,
                StartsAtUtc = playEvent.Cycle?.StartsAtUtc ?? default,
                EndsAtUtc = playEvent.Cycle?.EndsAtUtc ?? default,
                State = WireEnum.ToWire(cycleState),
                IsCancelled = playEvent.CancelledAtUtc is not null,
                BannerAddress = playEvent.BannerAddress,
                AccentColor = playEvent.AccentColor,
                SortOrder = playEvent.SortOrder,
                PrizeCohort = playEvent.PrizeCohort.ToString(),
                Participants = playEvent.Cycle?.TotalRanked ?? 0,
                EntryRules = new PlayEventRulesDto
                {
                    MaxEntriesPerDay = playEvent.MaxEntriesPerDay,
                    MaxEntriesTotal = playEvent.MaxEntriesTotal,
                    MinGradeOrder = playEvent.MinGradeOrder,
                    MaxGradeOrder = playEvent.MaxGradeOrder,
                    MinLevel = playEvent.MinLevel,
                    RequiresSku = playEvent.EntryProduct?.Key
                },
                Prizes = playEvent.PrizeTiers
                    .OrderBy(t => t.SortOrder)
                    .ThenBy(t => t.FromRank)
                    .Select(t =>
                    {
                        var prizeText = t.Translations.FirstOrDefault(x => x.LangId == langId);

                        return new EventPrizeTierDto
                        {
                            TierId = t.Id,
                            FromRank = t.FromRank,
                            ToRank = t.ToRank,
                            Kind = WireEnum.ToWire(t.Kind),
                            Title = prizeText?.Title ?? string.Empty,
                            Description = prizeText?.Description ?? string.Empty,
                            Quantity = t.Quantity,
                            SortOrder = t.SortOrder
                        };
                    })
                    .ToList(),
                Eligible = ineligible is null,
                IneligibleCode = ineligible,
                EntriesToday = used?.Today ?? 0,
                EntriesTotal = used?.Total ?? 0,
                MyRank = standing is { Rank: > 0 } ? standing.Rank : null,
                MyValue = standing?.Value
            });
        }

        return result;
    }

    /// <summary>
    /// Why this account cannot enter, as the same <c>PC_*</c> code the run-start refusal would carry,
    /// or null when it can.
    /// <para>
    /// The same order the resolver checks in, so the card and the refusal never disagree about which
    /// reason to show — "this event is for another year" and "you are out of entries today" are
    /// different sentences, and showing the wrong one is how a support ticket starts.
    /// </para>
    /// </summary>
    private static string? IneligibleCodeFor(
        PlayEvent playEvent,
        LeaderboardCycleState cycleState,
        int gradeOrder,
        int level,
        HashSet<Guid> ownedProducts,
        int entriesToday,
        int entriesTotal)
    {
        if (!playEvent.AcceptsEntries(cycleState))
            return ApiErrors.PlayEventClosed.Code;

        if (playEvent.MinGradeOrder > 0 && gradeOrder < playEvent.MinGradeOrder
            || playEvent.MaxGradeOrder > 0 && gradeOrder > playEvent.MaxGradeOrder)
            return ApiErrors.PlayEventNotEligible.Code;

        if (playEvent.MinLevel > 0 && level < playEvent.MinLevel)
            return ApiErrors.PlayEventNotEligible.Code;

        if (playEvent.EntryProductId is { } productId && !ownedProducts.Contains(productId))
            return ApiErrors.PlayEventNotEligible.Code;

        if (playEvent.MaxEntriesPerDay is { } perDay && entriesToday >= perDay)
            return ApiErrors.PlayEventEntryLimit.Code;

        if (playEvent.MaxEntriesTotal is { } total && entriesTotal >= total)
            return ApiErrors.PlayEventEntryLimit.Code;

        return null;
    }

    private static EventAwardDto Map(EventAward award, Guid langId)
    {
        var eventText = award.Event?.Translations.FirstOrDefault(t => t.LangId == langId);
        var prizeText = award.Tier?.Translations.FirstOrDefault(t => t.LangId == langId);

        return new EventAwardDto
        {
            AwardId = award.Id,
            EventId = award.EventId,
            EventName = eventText?.Name ?? string.Empty,
            PrizeTitle = prizeText?.Title ?? string.Empty,
            PrizeDescription = prizeText?.Description ?? string.Empty,
            Kind = WireEnum.ToWire(award.Tier?.Kind ?? EventPrizeKind.InGame),
            FinalRank = award.FinalRank,
            Value = award.Value,
            State = WireEnum.ToWire(award.State),
            ClaimState = award.Claim is { } claim ? WireEnum.ToWire(claim.State) : null,
            ClaimExpiresAtUtc = award.Claim?.ExpiresAtUtc,
            AwardedAtUtc = award.CreatedAtUtc,
            SeenAtUtc = award.SeenAtUtc
        };
    }
}
