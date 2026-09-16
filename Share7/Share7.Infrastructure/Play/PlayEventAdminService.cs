using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Economy;
using Share7.Domain.Leaderboards;
using Share7.Domain.Play;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Authoring competitions, their ladders and their prize tables.
/// <para>
/// <b>Three objects are written as one.</b> An event, its board and that board's single cycle are
/// created in one transaction, because each is useless without the others: a ladder nobody enters, an
/// event with nowhere to rank, or a prize table paying against ranks that do not exist. The window is
/// authored once here and lives on the cycle from then on.
/// </para>
/// <para>
/// <b>What may be changed narrows as the event runs.</b> Before it opens, almost everything; while it
/// is open, the end date may only move later and the prize table is frozen; once it has closed,
/// nothing but presentation. Entrants competed under the rules they were shown, and an operator
/// editing the prize table on the last day would be changing the deal after the fact.
/// </para>
/// </summary>
public class PlayEventAdminService : IPlayEventAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public PlayEventAdminService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<PlayEventAdminDto>> ListForAuthoringAsync(
        Guid? gameId = null, bool includeFinished = true, CancellationToken cancellationToken = default)
    {
        var query = BaseQuery();

        if (gameId is { } id) query = query.Where(e => e.GameId == id);

        if (!includeFinished)
            query = query.Where(e => e.Cycle!.State != LeaderboardCycleState.Settled);

        var events = await query
            .OrderByDescending(e => e.Cycle!.StartsAtUtc)
            .ToListAsync(cancellationToken);

        if (events.Count == 0) return [];

        var ids = events.Select(e => e.Id).ToList();

        var awardCounts = await _dbContext.EventAwards
            .AsNoTracking()
            .Where(a => ids.Contains(a.EventId))
            .GroupBy(a => a.EventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EventId, x => x.Count, cancellationToken);

        return events.Select(e => Map(e, awardCounts.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<PlayEventAdminDto?> GetForAuthoringAsync(
        Guid eventId, CancellationToken cancellationToken = default)
    {
        var playEvent = await BaseQuery().FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (playEvent is null) return null;

        var awards = await _dbContext.EventAwards.CountAsync(a => a.EventId == eventId, cancellationToken);

        return Map(playEvent, awards);
    }

    public async Task<ServiceResult<PlayEventAdminDto>> CreateAsync(
        SavePlayEventRequest request, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, null, cancellationToken);

        if (!validated.Succeeded) return Propagate<PlayEventAdminDto>(validated);

        var v = validated.Value!;
        var now = DateTime.UtcNow;
        var eventId = Guid.NewGuid();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The ladder. Period Event, so the rollover service leaves it alone — its window was authored
        // rather than derived, and nothing should be opening a second one next week.
        var board = new LeaderboardBoard
        {
            Id = Guid.NewGuid(),
            BoardKey = v.BoardKey,
            GameId = request.GameId,
            ModeId = request.ModeId,
            EventId = eventId,
            Metric = v.Metric,
            SortDirection = v.SortDirection,
            Aggregation = v.Aggregation,
            Period = LeaderboardPeriod.Event,
            SupportedCohorts = "All,Grade",
            VisibleRankLimit = null,
            IsActive = true,
            GraceSeconds = 60,
            CreatedAtUtc = now,
            Translations = v.Names
                .Select(n => new LeaderboardBoardTranslation
                {
                    Id = Guid.NewGuid(),
                    LangId = n.LangId,
                    Name = n.Name,
                    Description = n.Description
                })
                .ToList()
        };

        var cycle = new LeaderboardCycle
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,

            // Opened immediately when the window has already begun, so an event authored to start
            // "now" accepts its first entry rather than waiting for the next rollover pass.
            State = request.StartsAtUtc <= now
                ? LeaderboardCycleState.Open
                : LeaderboardCycleState.Scheduled,
            CreatedAtUtc = now
        };

        _dbContext.LeaderboardBoards.Add(board);
        _dbContext.LeaderboardCycles.Add(cycle);

        var playEvent = new PlayEvent
        {
            Id = eventId,
            EventKey = v.EventKey,
            GameId = request.GameId,
            ModeId = request.ModeId,
            WorldKey = v.WorldKey,
            GrantsWorldForDuration = request.GrantsWorldForDuration,
            BoardId = board.Id,
            CycleId = cycle.Id,
            PrizeCohort = v.PrizeCohort,
            MaxEntriesPerDay = request.MaxEntriesPerDay,
            MaxEntriesTotal = request.MaxEntriesTotal,
            MinGradeOrder = request.MinGradeOrder,
            MaxGradeOrder = request.MaxGradeOrder,
            MinLevel = request.MinLevel,
            EntryProductId = request.EntryProductId,
            ClaimWindowDays = request.ClaimWindowDays,
            EconomyProfileId = request.EconomyProfileId,
            BannerAddress = request.BannerAddress,
            AccentColor = request.AccentColor,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now,
            Translations = request.Translations
                .Select(t => new PlayEventTranslation
                {
                    EventId = eventId,
                    LangId = t.LangId,
                    Name = t.Name.Trim(),
                    Description = (t.Description ?? string.Empty).Trim(),
                    Rules = (t.Rules ?? string.Empty).Trim()
                })
                .ToList()
        };

        _dbContext.PlayEvents.Add(playEvent);

        await WriteTiersAsync(playEvent, request.PrizeTiers, v, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult<PlayEventAdminDto>.Success(
            (await GetForAuthoringAsync(eventId, cancellationToken))!);
    }

    public async Task<ServiceResult<PlayEventAdminDto>> UpdateAsync(
        Guid eventId, SavePlayEventRequest request, CancellationToken cancellationToken = default)
    {
        // The tiers' reward rules come with them: what a tier pays is the part of the prize table
        // SamePrizeTable has to compare, and without them every amount reads as unchanged.
        var playEvent = await _dbContext.PlayEvents
            .Include(e => e.Translations)
            .Include(e => e.PrizeTiers).ThenInclude(t => t.Translations)
            .Include(e => e.PrizeTiers).ThenInclude(t => t.RewardRule!).ThenInclude(r => r.Grants).ThenInclude(g => g.Currency)
            .Include(e => e.PrizeTiers).ThenInclude(t => t.RewardRule!).ThenInclude(r => r.EntitlementGrants)
            .Include(e => e.Cycle)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (playEvent is null)
            return ServiceResult<PlayEventAdminDto>.Failure(
                ApiErrors.PlayEventUnknown, ServiceErrorKind.NotFound, "No event has that id.");

        if (!string.Equals(playEvent.EventKey, (request.EventKey ?? string.Empty).Trim(), StringComparison.Ordinal))
            return ServiceResult<PlayEventAdminDto>.Failure(
                ApiErrors.PlayEventInvalid,
                ServiceErrorKind.Conflict,
                "An event key is immutable — awards and analytics name it. Author a new event instead.");

        var state = playEvent.Cycle?.State ?? LeaderboardCycleState.Scheduled;
        var started = state != LeaderboardCycleState.Scheduled;
        var finished = state is LeaderboardCycleState.Closed or LeaderboardCycleState.Settled;

        var validated = await ValidateAsync(request, eventId, cancellationToken);

        if (!validated.Succeeded) return Propagate<PlayEventAdminDto>(validated);

        var v = validated.Value!;

        if (started)
        {
            if (playEvent.ModeId != request.ModeId
                || !string.Equals(playEvent.WorldKey, v.WorldKey, StringComparison.Ordinal)
                || playEvent.PrizeCohort != v.PrizeCohort)
                return Frozen("what an entry is played in and ranked within");

            if (playEvent.Cycle is { } cycle && cycle.StartsAtUtc != request.StartsAtUtc)
                return Frozen("the start of a running event");

            // The end may move later — extending a competition is a decision an operator can make in
            // public — but never earlier, which would retire a ladder people are still climbing.
            if (playEvent.Cycle is { } live && request.EndsAtUtc < live.EndsAtUtc)
                return Frozen("the end of a running event, which may only be extended");
        }

        if (finished && !SamePrizeTable(playEvent, request))
            return Frozen("the prize table of a finished event");

        if (started && !SamePrizeTable(playEvent, request))
            return Frozen("the prize table of a running event");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        playEvent.ModeId = request.ModeId;
        playEvent.WorldKey = v.WorldKey;
        playEvent.GrantsWorldForDuration = request.GrantsWorldForDuration;
        playEvent.PrizeCohort = v.PrizeCohort;
        playEvent.MaxEntriesPerDay = request.MaxEntriesPerDay;
        playEvent.MaxEntriesTotal = request.MaxEntriesTotal;
        playEvent.MinGradeOrder = request.MinGradeOrder;
        playEvent.MaxGradeOrder = request.MaxGradeOrder;
        playEvent.MinLevel = request.MinLevel;
        playEvent.EntryProductId = request.EntryProductId;
        playEvent.ClaimWindowDays = request.ClaimWindowDays;
        playEvent.EconomyProfileId = request.EconomyProfileId;
        playEvent.BannerAddress = request.BannerAddress;
        playEvent.AccentColor = request.AccentColor;
        playEvent.SortOrder = request.SortOrder;
        playEvent.IsActive = request.IsActive;
        playEvent.UpdatedAtUtc = DateTime.UtcNow;

        _dbContext.PlayEventTranslations.RemoveRange(playEvent.Translations);
        playEvent.Translations.Clear();

        foreach (var translation in request.Translations)
        {
            playEvent.Translations.Add(new PlayEventTranslation
            {
                EventId = playEvent.Id,
                LangId = translation.LangId,
                Name = translation.Name.Trim(),
                Description = (translation.Description ?? string.Empty).Trim(),
                Rules = (translation.Rules ?? string.Empty).Trim()
            });
        }

        if (playEvent.Cycle is { } window)
        {
            window.StartsAtUtc = request.StartsAtUtc;
            window.EndsAtUtc = request.EndsAtUtc;

            // A window moved back into the future re-opens as scheduled; one that now covers today
            // opens. Anything already closed or settled is left exactly as it is.
            if (window.State is LeaderboardCycleState.Scheduled or LeaderboardCycleState.Open)
                window.State = request.StartsAtUtc <= DateTime.UtcNow
                    ? LeaderboardCycleState.Open
                    : LeaderboardCycleState.Scheduled;
        }

        if (!started)
        {
            // Only a scheduled event's prize table can be rewritten, and it is rewritten wholesale
            // rather than merged: a half-updated table is a prize nobody can explain.
            await ClearTiersAsync(playEvent, cancellationToken);
            await WriteTiersAsync(playEvent, request.PrizeTiers, v, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult<PlayEventAdminDto>.Success(
            (await GetForAuthoringAsync(eventId, cancellationToken))!);
    }

    public async Task<ServiceResult<PlayEventAdminDto>> CancelAsync(
        Guid eventId, string? reason, CancellationToken cancellationToken = default)
    {
        var playEvent = await _dbContext.PlayEvents
            .Include(e => e.Cycle)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (playEvent is null)
            return ServiceResult<PlayEventAdminDto>.Failure(
                ApiErrors.PlayEventUnknown, ServiceErrorKind.NotFound, "No event has that id.");

        if (playEvent.CancelledAtUtc is not null)
            return ServiceResult<PlayEventAdminDto>.Success(
                (await GetForAuthoringAsync(eventId, cancellationToken))!);

        var now = DateTime.UtcNow;

        playEvent.CancelledAtUtc = now;
        playEvent.CancelReason = reason;
        playEvent.IsActive = false;
        playEvent.UpdatedAtUtc = now;

        // The ladder closes with it. Entries already recorded keep their ranks — they are history,
        // and a child who played deserves to see where they got to — but nothing more projects and
        // the prize observer refuses to award a cancelled event.
        if (playEvent.Cycle is { State: LeaderboardCycleState.Open or LeaderboardCycleState.Scheduled } cycle)
        {
            cycle.State = LeaderboardCycleState.Closed;
            cycle.ClosedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<PlayEventAdminDto>.Success(
            (await GetForAuthoringAsync(eventId, cancellationToken))!);
    }

    public async Task<ServiceResult<PlayEventAdminDto>> DuplicateAsync(
        Guid eventId, Guid createdByUserId, CancellationToken cancellationToken = default)
    {
        var source = await BaseQuery().FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (source is null)
            return ServiceResult<PlayEventAdminDto>.Failure(
                ApiErrors.PlayEventUnknown, ServiceErrorKind.NotFound, "No event has that id.");

        var window = source.Cycle;

        if (window is null)
            return ServiceResult<PlayEventAdminDto>.Failure(
                ApiErrors.PlayEventInvalid, ServiceErrorKind.Conflict, "That event has no window to copy.");

        // The next run starts where this one ends and lasts as long, which is what "same event, next
        // week" means. The operator can move it afterwards; this only has to save them retyping it.
        var length = window.EndsAtUtc - window.StartsAtUtc;
        var startsAt = window.EndsAtUtc;

        var request = new SavePlayEventRequest
        {
            EventKey = NextKey(source.EventKey),
            GameId = source.GameId,
            ModeId = source.ModeId,
            WorldKey = source.WorldKey,
            GrantsWorldForDuration = source.GrantsWorldForDuration,
            Metric = source.Board?.Metric ?? string.Empty,
            Aggregation = source.Board?.Aggregation == LeaderboardAggregation.Sum ? "sum" : "best",
            StartsAtUtc = startsAt,
            EndsAtUtc = startsAt + length,
            PrizeCohort = source.PrizeCohort.ToString().ToLowerInvariant(),
            MaxEntriesPerDay = source.MaxEntriesPerDay,
            MaxEntriesTotal = source.MaxEntriesTotal,
            MinGradeOrder = source.MinGradeOrder,
            MaxGradeOrder = source.MaxGradeOrder,
            MinLevel = source.MinLevel,
            EntryProductId = source.EntryProductId,
            ClaimWindowDays = source.ClaimWindowDays,
            EconomyProfileId = source.EconomyProfileId,
            BannerAddress = source.BannerAddress,
            AccentColor = source.AccentColor,
            SortOrder = source.SortOrder,

            // Copied as inactive: a duplicate that went live the moment it was made would publish an
            // event nobody had read through yet.
            IsActive = false,
            Translations = source.Translations
                .Select(t => new PlayEventTranslationRequest
                {
                    LangId = t.LangId,
                    Name = t.Name,
                    Description = t.Description,
                    Rules = t.Rules
                })
                .ToList(),
            PrizeTiers = source.PrizeTiers
                .OrderBy(t => t.SortOrder)
                .Select(t => new SaveEventPrizeTierRequest
                {
                    FromRank = t.FromRank,
                    ToRank = t.ToRank,
                    Kind = WireEnum.ToWire(t.Kind).ToLowerInvariant(),
                    DeclaredValueMinor = t.DeclaredValueMinor,
                    ValueCurrencyCode = t.ValueCurrencyCode,
                    Quantity = t.Quantity,
                    SortOrder = t.SortOrder,
                    Grants = GrantsOf(t),
                    Translations = t.Translations
                        .Select(x => new EventPrizeTierTranslationRequest
                        {
                            LangId = x.LangId,
                            Title = x.Title,
                            Description = x.Description
                        })
                        .ToList()
                })
                .ToList()
        };

        return await CreateAsync(request, createdByUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<EventAwardDto>> GetAwardsAsync(
        Guid eventId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var awards = await _dbContext.EventAwards
            .AsNoTracking()
            .Include(a => a.Event).ThenInclude(e => e!.Translations)
            .Include(a => a.Tier).ThenInclude(t => t!.Translations)
            .Include(a => a.Claim)
            .Where(a => a.EventId == eventId)
            .OrderBy(a => a.Cohort)
            .ThenBy(a => a.FinalRank)
            .ToListAsync(cancellationToken);

        return awards
            .Select(a => new EventAwardDto
            {
                AwardId = a.Id,
                EventId = a.EventId,
                EventName = a.Event?.Translations.FirstOrDefault(t => t.LangId == langId)?.Name ?? string.Empty,
                PrizeTitle = a.Tier?.Translations.FirstOrDefault(t => t.LangId == langId)?.Title ?? string.Empty,
                PrizeDescription = a.Tier?.Translations.FirstOrDefault(t => t.LangId == langId)?.Description ?? string.Empty,
                Kind = WireEnum.ToWire(a.Tier?.Kind ?? EventPrizeKind.InGame),
                FinalRank = a.FinalRank,
                Value = a.Value,
                State = WireEnum.ToWire(a.State),
                ClaimState = a.Claim is { } claim ? WireEnum.ToWire(claim.State) : null,
                ClaimExpiresAtUtc = a.Claim?.ExpiresAtUtc,
                AwardedAtUtc = a.CreatedAtUtc,
                SeenAtUtc = a.SeenAtUtc
            })
            .ToList();
    }

    // ------------------------------------------------------------- prize tiers

    /// <summary>
    /// Writes a prize table, minting the backing reward rule each in-game tier pays through.
    /// <para>
    /// The rules are owned by the tiers rather than authored by hand in the rewards console: an
    /// operator writing an event should not have to understand the reward engine, and a rule left
    /// behind by a deleted tier is a prize that pays for an event nobody is running.
    /// </para>
    /// </summary>
    private async Task WriteTiersAsync(
        PlayEvent playEvent,
        List<SaveEventPrizeTierRequest> tiers,
        Validated validated,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        foreach (var request in tiers)
        {
            var tierId = Guid.NewGuid();
            var kind = WireEnum.FromWire<EventPrizeKind>(request.Kind);

            Guid? ruleId = null;

            if (kind == EventPrizeKind.InGame && request.Grants.Count > 0)
            {
                var rule = new RewardRule
                {
                    Id = Guid.NewGuid(),
                    Name = $"Event prize · {playEvent.EventKey} · ranks {request.FromRank}-{request.ToRank}",
                    EventType = RewardEventType.EventPrize,

                    // Scoped to this tier, so nothing else can ever match it.
                    ReferenceKey = $"event:{playEvent.Id}:tier:{tierId}",

                    // One placing, one payment: the award row's own key is what enforces it, and the
                    // repeat policy only has to not fight that.
                    RepeatPolicy = RewardRepeatPolicy.Once,
                    TransactionType = CurrencyTransactionType.GameReward,
                    Enabled = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                foreach (var grant in request.Grants)
                {
                    if (grant.ProductId is { } productId)
                    {
                        rule.EntitlementGrants.Add(new RewardRuleEntitlementGrant
                        {
                            Id = Guid.NewGuid(),
                            RewardRuleId = rule.Id,
                            ProductId = productId
                        });

                        continue;
                    }

                    if (grant.Amount <= 0) continue;

                    if (validated.Currencies.TryGetValue(grant.Currency ?? string.Empty, out var currencyId))
                    {
                        rule.Grants.Add(new RewardRuleGrant
                        {
                            Id = Guid.NewGuid(),
                            RewardRuleId = rule.Id,
                            CurrencyId = currencyId,
                            Amount = grant.Amount
                        });
                    }
                }

                _dbContext.RewardRules.Add(rule);
                ruleId = rule.Id;
            }

            _dbContext.EventPrizeTiers.Add(new EventPrizeTier
            {
                Id = tierId,
                EventId = playEvent.Id,
                FromRank = request.FromRank,
                ToRank = request.ToRank,
                Kind = kind,
                RewardRuleId = ruleId,
                DeclaredValueMinor = kind == EventPrizeKind.RealWorld ? request.DeclaredValueMinor : null,
                ValueCurrencyCode = kind == EventPrizeKind.RealWorld ? request.ValueCurrencyCode : null,
                Quantity = request.Quantity,
                SortOrder = request.SortOrder,
                Translations = request.Translations
                    .Select(t => new EventPrizeTierTranslation
                    {
                        TierId = tierId,
                        LangId = t.LangId,
                        Title = t.Title.Trim(),
                        Description = (t.Description ?? string.Empty).Trim()
                    })
                    .ToList()
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>Drops a scheduled event's prize table, and the reward rules that existed only to pay it.</summary>
    private async Task ClearTiersAsync(PlayEvent playEvent, CancellationToken cancellationToken)
    {
        var ruleIds = playEvent.PrizeTiers
            .Where(t => t.RewardRuleId is not null)
            .Select(t => t.RewardRuleId!.Value)
            .ToList();

        _dbContext.EventPrizeTiers.RemoveRange(playEvent.PrizeTiers);
        playEvent.PrizeTiers.Clear();

        if (ruleIds.Count == 0) return;

        var rules = await _dbContext.RewardRules
            .Where(r => ruleIds.Contains(r.Id) && r.EventType == RewardEventType.EventPrize)
            .ToListAsync(cancellationToken);

        _dbContext.RewardRules.RemoveRange(rules);
    }

    private static List<EventPrizeGrantRequest> GrantsOf(EventPrizeTier tier) =>
        tier.RewardRule is null
            ? []
            : tier.RewardRule.Grants
                .Select(g => new EventPrizeGrantRequest { Currency = g.Currency?.Key, Amount = g.Amount })
                .Concat(tier.RewardRule.EntitlementGrants
                    .Select(g => new EventPrizeGrantRequest { ProductId = g.ProductId }))
                .ToList();

    /// <summary>
    /// Whether the request's prize table is the one already stored — used to tell "did not touch the
    /// prizes" from "tried to edit them", so an operator fixing a typo in the blurb of a running event
    /// is not refused for sending the table back unchanged.
    /// </summary>
    private static bool SamePrizeTable(PlayEvent playEvent, SavePlayEventRequest request)
    {
        if (playEvent.PrizeTiers.Count != request.PrizeTiers.Count) return false;

        var stored = playEvent.PrizeTiers.OrderBy(t => t.FromRank).ThenBy(t => t.ToRank).ToList();
        var sent = request.PrizeTiers.OrderBy(t => t.FromRank).ThenBy(t => t.ToRank).ToList();

        for (var i = 0; i < stored.Count; i++)
        {
            if (stored[i].FromRank != sent[i].FromRank
                || stored[i].ToRank != sent[i].ToRank
                || stored[i].Quantity != sent[i].Quantity
                || stored[i].Kind != WireEnum.FromWire<EventPrizeKind>(sent[i].Kind)
                || stored[i].DeclaredValueMinor != (stored[i].Kind == EventPrizeKind.RealWorld ? sent[i].DeclaredValueMinor : null)
                || !SameGrants(GrantsOf(stored[i]), sent[i].Grants))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether two tiers hand over the same things. The amounts are the prize — ranks and kinds
    /// alone let 500 coins become 5 on a running event while the table still read as untouched.
    /// Zero-amount currency lines are ignored because <see cref="WriteTiersAsync"/> never stores them.
    /// </summary>
    private static bool SameGrants(List<EventPrizeGrantRequest> stored, List<EventPrizeGrantRequest> sent)
    {
        static IEnumerable<string> Normalise(IEnumerable<EventPrizeGrantRequest> grants) =>
            grants
                .Where(g => g.ProductId is not null || g.Amount > 0)
                .Select(g => g.ProductId is { } productId
                    ? $"product:{productId:N}"
                    : $"currency:{(g.Currency ?? string.Empty).Trim()}:{g.Amount}")
                .OrderBy(k => k, StringComparer.Ordinal);

        return Normalise(stored).SequenceEqual(Normalise(sent ?? []), StringComparer.Ordinal);
    }

    // ------------------------------------------------------------- validation

    private sealed record Validated(
        string EventKey,
        string BoardKey,
        string Metric,
        string? WorldKey,
        LeaderboardCohort PrizeCohort,
        LeaderboardAggregation Aggregation,
        LeaderboardSortDirection SortDirection,
        List<BoardName> Names,
        Dictionary<string, Guid> Currencies);

    private sealed record BoardName(Guid LangId, string Name, string Description);

    private async Task<ServiceResult<Validated>> ValidateAsync(
        SavePlayEventRequest request, Guid? existingEventId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var key = (request.EventKey ?? string.Empty).Trim().ToLowerInvariant();

        if (key.Length == 0)
            errors.Add("eventKey is required.");
        else if (await _dbContext.PlayEvents.AnyAsync(
                     e => e.EventKey == key && (existingEventId == null || e.Id != existingEventId),
                     cancellationToken))
            errors.Add($"Another event already uses the key '{key}'.");

        var mode = await _dbContext.GameModes
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.ModeId, cancellationToken);

        if (mode is null)
            errors.Add("The event names no mode that exists.");
        else if (mode.GameId != request.GameId)
            errors.Add("That mode belongs to a different game.");

        if (request.EndsAtUtc <= request.StartsAtUtc)
            errors.Add("endsAtUtc must be after startsAtUtc.");

        if (!LeaderboardMetrics.IsKnown(request.Metric))
            errors.Add($"'{request.Metric}' is not a metric anything raises.");

        var worldKey = string.IsNullOrWhiteSpace(request.WorldKey) ? null : request.WorldKey.Trim();

        if (worldKey is not null
            && !await _dbContext.GameWorlds.AnyAsync(
                w => w.GameId == request.GameId && w.WorldKey == worldKey, cancellationToken))
            errors.Add($"'{worldKey}' is not a world this game offers.");

        var cohort = (request.PrizeCohort ?? "all").Trim().ToLowerInvariant() switch
        {
            "grade" => LeaderboardCohort.Grade,
            _ => LeaderboardCohort.All
        };

        var aggregation = (request.Aggregation ?? "best").Trim().ToLowerInvariant() == "sum"
            ? LeaderboardAggregation.Sum
            : LeaderboardAggregation.Best;

        // Times rank ascending — the smaller number is the better result — and everything else
        // descending. Derived rather than authored so an operator cannot create a ladder where the
        // slowest run wins.
        var direction = request.Metric is LeaderboardMetrics.BestRunSeconds
            ? LeaderboardSortDirection.Asc
            : LeaderboardSortDirection.Desc;

        if (request.EntryProductId is { } entryProductId
            && !await _dbContext.Products.AnyAsync(p => p.Id == entryProductId, cancellationToken))
            errors.Add($"No product has id {entryProductId}.");

        if (request.EconomyProfileId is { } profileId
            && !await _dbContext.EconomyProfiles.AnyAsync(p => p.Id == profileId, cancellationToken))
            errors.Add($"No economy profile has id {profileId}.");

        if (request.MaxGradeOrder > 0 && request.MinGradeOrder > request.MaxGradeOrder)
            errors.Add("minGradeOrder cannot be above maxGradeOrder.");

        errors.AddRange(await ValidateTiersAsync(request, cancellationToken));

        var languages = await _dbContext.Languages.Select(l => new { l.Id, l.Code }).ToListAsync(cancellationToken);
        var names = new List<BoardName>();

        foreach (var translation in request.Translations ?? [])
        {
            var name = (translation.Name ?? string.Empty).Trim();

            if (name.Length == 0)
                errors.Add($"name is required for language {translation.LangId}.");
            else
                names.Add(new BoardName(translation.LangId, name, (translation.Description ?? string.Empty).Trim()));
        }

        var missing = languages.Where(l => names.All(n => n.LangId != l.Id)).Select(l => l.Code).ToList();

        if (missing.Count > 0)
            errors.Add($"A name is required for every language. Missing: {string.Join(", ", missing)}.");

        var currencyKeys = (request.PrizeTiers ?? [])
            .SelectMany(t => t.Grants)
            .Where(g => !string.IsNullOrWhiteSpace(g.Currency))
            .Select(g => g.Currency!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var currencies = currencyKeys.Count == 0
            ? new Dictionary<string, Guid>(StringComparer.Ordinal)
            : await _dbContext.Currencies
                .AsNoTracking()
                .Where(c => currencyKeys.Contains(c.Key))
                .ToDictionaryAsync(c => c.Key, c => c.Id, StringComparer.Ordinal, cancellationToken);

        foreach (var unknown in currencyKeys.Where(k => !currencies.ContainsKey(k)))
            errors.Add($"'{unknown}' is not a currency.");

        // The board key is derived, never authored: it has to be unique, stable, and short enough
        // that "{boardKey}:{band}" still fits a reward rule's reference key.
        var boardKey = $"event.{key}";

        if (boardKey.Length > 110)
            errors.Add("eventKey is too long — the derived board key must fit in 110 characters.");
        else if (await _dbContext.LeaderboardBoards.AnyAsync(
                     b => b.BoardKey == boardKey && (existingEventId == null || b.EventId != existingEventId),
                     cancellationToken))
            errors.Add($"A board already exists for the key '{boardKey}'.");

        return errors.Count > 0
            ? ServiceResult<Validated>.Failure(
                ApiErrors.PlayEventInvalid,
                ServiceErrorKind.Validation,
                string.Join(" ", errors),
                new Dictionary<string, object?> { ["problems"] = errors })
            : ServiceResult<Validated>.Success(new Validated(
                key, boardKey, request.Metric, worldKey, cohort, aggregation, direction, names, currencies));
    }

    private async Task<List<string>> ValidateTiersAsync(
        SavePlayEventRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var tiers = request.PrizeTiers ?? [];

        var hasRealWorld = tiers.Any(t =>
            WireEnum.FromWire<EventPrizeKind>(t.Kind) == EventPrizeKind.RealWorld);

        // **A prize of real value plus a price of entry is a paid competition**, which is a
        // different legal object in most of the world and not one this platform offers to children.
        // Refused at authoring rather than caught at entry, because by then it has been advertised.
        if (hasRealWorld && request.EntryProductId is not null)
            errors.Add(
                "An event with a real-world prize must be free to enter. Remove the entry product, " +
                "or make the prize in-game.");

        for (var i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            var kind = WireEnum.FromWire<EventPrizeKind>(tier.Kind);

            if (tier.FromRank > tier.ToRank)
                errors.Add($"Prize tier {i + 1} ends at a better rank than it starts.");

            if (kind == EventPrizeKind.InGame && tier.Grants.Count == 0)
                errors.Add($"Prize tier {i + 1} is in-game but hands over nothing.");

            if (kind == EventPrizeKind.RealWorld && tier.Grants.Count > 0)
                errors.Add($"Prize tier {i + 1} is real-world; it cannot also grant currency or products.");

            foreach (var productId in tier.Grants.Where(g => g.ProductId is not null).Select(g => g.ProductId!.Value))
            {
                if (!await _dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken))
                    errors.Add($"Prize tier {i + 1} grants product {productId}, which does not exist.");
            }

            if (tier.Translations is null || tier.Translations.Count == 0)
                errors.Add($"Prize tier {i + 1} has no title.");

            // Overlapping bands are refused rather than resolved. The award path picks the narrowest
            // covering tier, so an overlap would still pay exactly one prize — but which one would
            // depend on how the operator happened to write the ranges, and a prize table is a promise.
            for (var j = i + 1; j < tiers.Count; j++)
            {
                if (tier.FromRank <= tiers[j].ToRank && tiers[j].FromRank <= tier.ToRank)
                    errors.Add($"Prize tiers {i + 1} and {j + 1} both cover the same ranks.");
            }
        }

        return errors;
    }

    // ------------------------------------------------------------- plumbing

    private IQueryable<PlayEvent> BaseQuery() =>
        _dbContext.PlayEvents
            .AsNoTracking()
            .Include(e => e.Translations)
            .Include(e => e.Game)
            .Include(e => e.Mode)
            .Include(e => e.Board)
            .Include(e => e.Cycle)
            .Include(e => e.EconomyProfile)
            .Include(e => e.PrizeTiers).ThenInclude(t => t.Translations)
            .Include(e => e.PrizeTiers).ThenInclude(t => t.RewardRule!).ThenInclude(r => r.Grants).ThenInclude(g => g.Currency)
            .Include(e => e.PrizeTiers).ThenInclude(t => t.RewardRule!).ThenInclude(r => r.EntitlementGrants);

    /// <summary>
    /// The next week's key, when the current one ends in a week number — <c>…2026w37</c> becomes
    /// <c>…2026w38</c>. Anything else gets a <c>-copy</c> suffix, which the operator can edit.
    /// </summary>
    private static string NextKey(string eventKey)
    {
        var separator = eventKey.LastIndexOf('w');

        if (separator > 0 && int.TryParse(eventKey[(separator + 1)..], out var week))
            return $"{eventKey[..(separator + 1)]}{week + 1}";

        return $"{eventKey}-copy";
    }

    private static ServiceResult<PlayEventAdminDto> Frozen(string what) =>
        ServiceResult<PlayEventAdminDto>.Failure(
            ApiErrors.PlayEventInvalid,
            ServiceErrorKind.Conflict,
            $"An event that has started cannot change {what} — entrants played under the rules they " +
            "were shown. Cancel it and author a new one instead.");

    private PlayEventAdminDto Map(PlayEvent playEvent, int awards) => new()
    {
        EventId = playEvent.Id,
        EventKey = playEvent.EventKey,
        GameId = playEvent.GameId,
        GameKey = playEvent.Game?.GameKey ?? string.Empty,
        ModeId = playEvent.ModeId,
        ModeKey = playEvent.Mode?.ModeKey ?? string.Empty,
        WorldKey = playEvent.WorldKey,
        GrantsWorldForDuration = playEvent.GrantsWorldForDuration,
        BoardId = playEvent.BoardId,
        BoardKey = playEvent.Board?.BoardKey ?? string.Empty,
        CycleId = playEvent.CycleId,
        Metric = playEvent.Board?.Metric ?? string.Empty,
        StartsAtUtc = playEvent.Cycle?.StartsAtUtc ?? default,
        EndsAtUtc = playEvent.Cycle?.EndsAtUtc ?? default,
        State = WireEnum.ToWire(playEvent.Cycle?.State ?? LeaderboardCycleState.Scheduled),
        PrizeCohort = playEvent.PrizeCohort.ToString(),
        MaxEntriesPerDay = playEvent.MaxEntriesPerDay,
        MaxEntriesTotal = playEvent.MaxEntriesTotal,
        MinGradeOrder = playEvent.MinGradeOrder,
        MaxGradeOrder = playEvent.MaxGradeOrder,
        MinLevel = playEvent.MinLevel,
        EntryProductId = playEvent.EntryProductId,
        ClaimWindowDays = playEvent.ClaimWindowDays,
        EconomyProfileId = playEvent.EconomyProfileId,
        EconomyProfileKey = playEvent.EconomyProfile?.ProfileKey ?? EconomyProfileKeys.Default,
        BannerAddress = playEvent.BannerAddress,
        AccentColor = playEvent.AccentColor,
        SortOrder = playEvent.SortOrder,
        IsActive = playEvent.IsActive,
        CancelledAtUtc = playEvent.CancelledAtUtc,
        CancelReason = playEvent.CancelReason,
        Participants = playEvent.Cycle?.TotalRanked ?? 0,
        AwardsIssued = awards,
        Translations = playEvent.Translations
            .OrderBy(t => t.LangId)
            .Select(t => new PlayEventTranslationRequest
            {
                LangId = t.LangId,
                Name = t.Name,
                Description = t.Description,
                Rules = t.Rules
            })
            .ToList(),
        PrizeTiers = playEvent.PrizeTiers
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.FromRank)
            .Select(t => new EventPrizeTierAdminDto
            {
                TierId = t.Id,
                FromRank = t.FromRank,
                ToRank = t.ToRank,
                Kind = WireEnum.ToWire(t.Kind).ToLowerInvariant(),
                Quantity = t.Quantity,
                SortOrder = t.SortOrder,
                DeclaredValueMinor = t.DeclaredValueMinor,
                ValueCurrencyCode = t.ValueCurrencyCode,
                Grants = GrantsOf(t),
                Translations = t.Translations
                    .OrderBy(x => x.LangId)
                    .Select(x => new EventPrizeTierTranslationRequest
                    {
                        LangId = x.LangId,
                        Title = x.Title,
                        Description = x.Description
                    })
                    .ToList()
            })
            .ToList()
    };

    private static ServiceResult<T> Propagate<T>(ServiceResult source) =>
        new() { ErrorKind = source.ErrorKind, Errors = source.Errors, Error = source.Error, Details = source.Details };
}
