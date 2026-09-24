using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Evidence.Interfaces;
using Share7.Application.Evidence.Models;
using Share7.Domain.Evidence;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Evidence;

/// <summary>
/// Writes the append-only educational evidence log.
/// <para>
/// Three things happen here and nowhere else: the contract is resolved (most-specific-wins), the
/// attempt ordinal is derived from the log itself rather than from anything a caller says, and the
/// rows are added to the ambient transaction without being saved.
/// </para>
/// </summary>
public class EvidenceRecorder : IEvidenceRecorder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<EvidenceRecorder> _logger;

    public EvidenceRecorder(ApplicationDbContext dbContext, ILogger<EvidenceRecorder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<EvidenceRecordingResult> RecordAsync(
        EvidenceRecordingContext context,
        IReadOnlyList<EvidenceAnswer> answers,
        CancellationToken cancellationToken = default)
    {
        if (answers.Count == 0)
            return EvidenceRecordingResult.Nothing;

        var contract = await ResolveContractAsync(context, cancellationToken);

        if (contract is null)
        {
            // Not an error. An interaction nobody has written a contract for is telemetry, which is
            // the default state of everything in the platform. Logged at debug so a *missing*
            // contract on a path that should have one is findable, without shouting about the
            // thousands of interactions that correctly have none.
            _logger.LogDebug(
                "No published evidence contract for interaction {Kind} on game {GameId} mode {ModeId} in context {Context}; nothing recorded.",
                context.InteractionKind, context.GameId, context.ModeId, context.PlayContext);
            return EvidenceRecordingResult.Nothing;
        }

        var localizationIds = answers.Select(a => a.ItemLocalizationId).Distinct().ToList();

        // Identity is resolved here and nowhere else. A caller knows which question it showed;
        // which *item* that question is a rendering of is a fact about the content, and letting a
        // caller assert it is how the two languages of one question became strangers in the first
        // place.
        var identities = await _dbContext.Questions
            .Where(q => localizationIds.Contains(q.Id))
            .Select(q => new { LocalizationId = q.Id, q.ItemVersionId, ItemId = q.ItemVersion!.ItemId })
            .ToDictionaryAsync(x => x.LocalizationId, cancellationToken);

        var itemIds = identities.Values.Select(x => x.ItemId).Distinct().ToList();

        // **Derived from the log, not from UserQuestionProgress.** The progress row counts attempts
        // per lesson and is overwritten; this counts responses per item and is not. One grouped
        // query, read before the inserts so the new rows do not count themselves.
        //
        // Grouped by **item**, which is a deliberate change of meaning: a child who answers the
        // same question in Arabic and then in English has answered one item twice, not two items
        // once, and reading it the old way inflated first-encounter evidence at every language
        // switch.
        var priorCounts = await _dbContext.LearnerResponses
            .Where(r => r.LearnerId == context.LearnerId && itemIds.Contains(r.ItemId))
            .GroupBy(r => r.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, cancellationToken);

        // Which organization owns this work, resolved once for the whole submission. A caller that
        // already knows — an assessment administration does — wins; everything else is derived from
        // the learner's own enrolments, because **the enrolment is what decides** (9.2).
        var orgId = context.OrgId ?? await ResolveOwningOrgAsync(context, cancellationToken);

        var strengths = new Dictionary<Guid, EvidenceStrength>(answers.Count);
        var recorded = 0;

        foreach (var answer in answers)
        {
            if (!identities.TryGetValue(answer.ItemLocalizationId, out var identity))
            {
                // A question the content layer does not recognise. Skipped rather than recorded
                // against a placeholder identity: evidence that names no item cannot be measured,
                // and writing it anyway would put an uninterpretable row above the recompute line.
                _logger.LogWarning(
                    "Evidence skipped for learner {LearnerId}: question {QuestionId} has no item identity.",
                    context.LearnerId, answer.ItemLocalizationId);
                continue;
            }

            var prior = priorCounts.GetValueOrDefault(identity.ItemId);
            var ordinal = prior + 1;
            var isFirst = ordinal == 1;

            // A game that did not say is read from the context: practice exists to be retried, and
            // everything else resolves one item once per administration.
            var retryPermitted = answer.RetryPermitted
                ?? context.PlayContext == PlayContextKind.Practice;

            // wasAided passed here for the same reason the projector passes it: an answer produced
            // with somebody leaning over the desk is not exam-grade. Without it this method reported
            // a strength the projector would then disagree with for exactly those responses.
            var strength = contract.StrengthFor(
                isFirst, answer.HintsUsed, retryPermitted, context.DeliveryMode, context.WasAided);

            strengths[answer.ItemLocalizationId] = strength;

            _dbContext.LearnerResponses.Add(new LearnerResponse
            {
                Id = Guid.NewGuid(),
                LearnerId = context.LearnerId,
                ItemId = identity.ItemId,
                ItemVersionId = identity.ItemVersionId,
                ItemLocalizationId = answer.ItemLocalizationId,
                LangId = context.LangId,

                ChoiceId = answer.ChoiceId,
                IsCorrect = answer.IsCorrect,
                WasUnrecognised = answer.WasUnrecognised,

                AttemptOrdinal = ordinal,
                IsFirstEncounter = isFirst,
                HintsUsed = answer.HintsUsed,
                ElapsedMs = Bound(answer.ElapsedMs),
                TimeLimitMs = Bound(answer.TimeLimitMs),
                RetryPermitted = retryPermitted,
                WasAided = context.WasAided,
                DeliveryMode = context.DeliveryMode,
                GroupId = context.GroupId,

                EvidenceContractVersionId = contract.Id,
                GameId = context.GameId,
                ModeId = context.ModeId,
                PlayContext = context.PlayContext,
                EventId = context.EventId,

                NodeId = context.NodeId,
                ContentVersion = context.ContentVersion,
                OrgId = orgId,

                OccurredAtUtc = answer.OccurredAtUtc ?? context.ReceivedAtUtc,
                ReceivedAtUtc = context.ReceivedAtUtc,
                IdempotencyKey = context.IdempotencyKey
            });

            // Two answers to the same item inside one submission are refused upstream, but the
            // counter is kept correct rather than trusting that to stay true.
            priorCounts[identity.ItemId] = ordinal;
            recorded++;
        }

        return new EvidenceRecordingResult
        {
            RecordedCount = recorded,
            ContractVersionId = contract.Id,
            StrengthByLocalization = strengths
        };
    }

    /// <summary>
    /// The organization whose enrolment this work was done under, or null for personal play.
    ///
    /// <para><b>This is the column the whole tenancy model filters on</b>, and stamping it here
    /// rather than at each call site is deliberate: there are two evidence-writing paths today and
    /// there will be more, and an org-scoped read is only as correct as the least careful of
    /// them.</para>
    ///
    /// <para><b>Derived from the enrolment, never from the account.</b> A learner with a
    /// school-issued account who works through a curriculum the school did not enrol them in has
    /// done personal work, and the school does not acquire it — which is the B2B2C rule the
    /// architecture spends a paragraph on and which costs one join here. Work outside any node, or
    /// under a curriculum version no org enrolled the learner in, stays null.</para>
    /// </summary>
    private async Task<Guid?> ResolveOwningOrgAsync(
        EvidenceRecordingContext context, CancellationToken cancellationToken)
    {
        if (context.NodeId is not { } nodeId) return null;

        var versionId = await _dbContext.CurriculumNodes
            .AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => (Guid?)n.CurriculumVersionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (versionId is null) return null;

        // Most recent wins where a learner is in two organizations studying the same curriculum —
        // a child who moved schools mid-year, most often. The alternative, refusing to choose,
        // would drop the evidence out of both schools' sight.
        return await _dbContext.Enrollments
            .AsNoTracking()
            .Where(e => e.LearnerId == context.LearnerId
                        && e.CurriculumVersionId == versionId
                        && e.OwnerOrgId != null
                        && e.EndedAtUtc == null)
            .OrderByDescending(e => e.StartedAtUtc)
            .Select(e => e.OwnerOrgId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Most-specific-wins: (game + mode) beats (game) beats (platform). A unique index makes two
    /// contracts at one specificity an insert failure, so the ordering below is total.
    /// </summary>
    private async Task<EvidenceContractVersion?> ResolveContractAsync(
        EvidenceRecordingContext context, CancellationToken cancellationToken)
    {
        var now = context.ReceivedAtUtc;

        var candidates = await _dbContext.EvidenceContracts
            .Where(c => c.InteractionKind == context.InteractionKind)
            .Where(c => c.GameId == null || c.GameId == context.GameId)
            .Where(c => c.ModeId == null || c.ModeId == context.ModeId)
            .Select(c => new
            {
                c.Id,
                c.GameId,
                c.ModeId,
                Versions = c.Versions
                    .Where(v => v.PublishedAtUtc != null && v.PublishedAtUtc <= now
                                && (v.RetiredAtUtc == null || v.RetiredAtUtc > now))
                    .OrderByDescending(v => v.VersionNumber)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var winner = candidates
            .Where(c => c.Versions.Count > 0)
            .OrderByDescending(c => c.GameId != null ? 2 : 0)
            .ThenByDescending(c => c.ModeId != null ? 1 : 0)
            .FirstOrDefault();

        var version = winner?.Versions[0];

        // A contract that does not admit this context is the same as no contract: an event entry
        // under a curriculum-only contract records nothing rather than recording something weaker.
        return version is not null && version.Admits(context.PlayContext) ? version : null;
    }

    /// <summary>
    /// A negative duration is a broken clock and an hour-long one is a backgrounded app. Both are
    /// stored as null rather than as a number that would quietly skew every timing statistic —
    /// "we do not know" is a different claim from "it took 3 milliseconds".
    /// </summary>
    private const int MaxPlausibleMs = 30 * 60 * 1000;

    private static int? Bound(int? ms) =>
        ms is { } value && value >= 0 && value <= MaxPlausibleMs ? value : null;
}
