using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Measurement.Interfaces;
using Share7.Domain.Evidence;
using Share7.Domain.Leaderboards;
using Share7.Domain.Measurement;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Measurement;

/// <inheritdoc cref="IObservationProjector"/>
public class ObservationProjector : IObservationProjector
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ObservationProjector> _logger;

    public ObservationProjector(ApplicationDbContext dbContext, ILogger<ObservationProjector> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<ObservationProjectionReport> ProjectForLearnerAsync(
        Guid learnerId, CancellationToken cancellationToken = default) =>
        ProjectAsync(learnerId, maxResponses: 2000, advanceWatermark: false, cancellationToken);

    public Task<ObservationProjectionReport> ProjectPendingAsync(
        int maxResponses = 5000, CancellationToken cancellationToken = default) =>
        ProjectAsync(learnerId: null, maxResponses, advanceWatermark: true, cancellationToken);

    public async Task<ReprojectionReport> ReprojectItemsAsync(
        IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        if (itemIds.Count == 0) return new ReprojectionReport();

        var ids = itemIds.Distinct().ToList();

        // Human decisions first, because the delete below is about to remove the rows carrying
        // them. An exclusion is the one thing on an observation that was never derived — somebody
        // looked at a mis-keyed item and said these answers do not count — and a rebuild that
        // forgot it would silently readmit every one of them.
        var exclusions = await _dbContext.Observations
            .AsNoTracking()
            .Where(o => ids.Contains(o.ItemId) && o.ExcludedAtUtc != null)
            .Select(o => new
            {
                o.LearnerResponseId,
                o.ItemVersionId,
                o.ExcludedAtUtc,
                o.ExclusionReason,
                o.ExcludedByUserId,
                o.ExclusionNote
            })
            .ToListAsync(cancellationToken);

        // Keyed by response rather than by (response, target): the old target is the one going
        // away, so a decision recorded against it has to carry to whatever replaces it.
        var carried = exclusions
            .Where(e => e.LearnerResponseId != null)
            .GroupBy(e => e.LearnerResponseId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var deleted = await _dbContext.Observations
            .Where(o => ids.Contains(o.ItemId))
            .ExecuteDeleteAsync(cancellationToken);

        var responses = await _dbContext.LearnerResponses
            .AsNoTracking()
            .Where(r => ids.Contains(r.ItemId))
            .OrderBy(r => r.Sequence)
            .Select(r => new Pending(
                r.Id, r.Sequence, r.LearnerId, r.ItemId, r.ItemVersionId,
                r.ChoiceId, r.IsCorrect, r.ChoiceId != null, r.IsFirstEncounter, r.HintsUsed,
                r.RetryPermitted, r.WasAided, r.DeliveryMode, r.GroupId, r.ElapsedMs,
                r.EvidenceContractVersionId, r.NodeId, r.OccurredAtUtc,
                r.Item!.Bank!.MaxEvidenceStrength))
            .ToListAsync(cancellationToken);

        if (responses.Count == 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ReprojectionReport { ItemsAffected = ids.Count, ObservationsDeleted = deleted };
        }

        var contracts = await LoadContractsAsync(responses, cancellationToken);
        var mappings = await LoadTargetMappingsAsync(responses, cancellationToken);

        var written = 0;
        var preserved = 0;

        foreach (var response in responses)
        {
            if (!mappings.TryGetValue(response.ItemId, out var targets) || targets.Count == 0)
                continue;

            var strength = StrengthFor(response, contracts);
            carried.TryGetValue(response.ResponseId, out var exclusion);

            foreach (var mapping in targets)
            {
                var observation = new Observation
                {
                    Id = Guid.NewGuid(),
                    LearnerResponseId = response.ResponseId,
                    LearnerId = response.LearnerId,
                    TargetId = mapping.TargetId,
                    ItemId = response.ItemId,
                    ItemVersionId = response.ItemVersionId,
                    Outcome = OutcomeFor(response),
                    Strength = strength,
                    Weight = mapping.Emphasis * WeightFor(response, contracts),
                    EvidenceContractVersionId = response.ContractVersionId,
                    GroupId = response.GroupId,
                    NodeId = response.NodeId,
                    ObservedAtUtc = response.OccurredAtUtc
                };

                if (exclusion is not null)
                {
                    observation.ExcludedAtUtc = exclusion.ExcludedAtUtc;
                    observation.ExclusionReason = exclusion.ExclusionReason;
                    observation.ExcludedByUserId = exclusion.ExcludedByUserId;
                    observation.ExclusionNote = exclusion.ExclusionNote;
                    preserved++;
                }

                _dbContext.Observations.Add(observation);
                written++;
            }
        }

        // Statistics are deliberately untouched. They describe the item — how hard it is, which
        // distractor pulls — and none of that changes because the claim it is evidence for was
        // renamed. Refolding would double every count.
        await _dbContext.SaveChangesAsync(cancellationToken);

        var learners = responses.Select(r => r.LearnerId).Distinct().Count();

        _logger.LogInformation(
            "Reprojected {Items} items: {Deleted} observations replaced by {Written} across {Learners} learners.",
            ids.Count, deleted, written, learners);

        return new ReprojectionReport
        {
            ItemsAffected = ids.Count,
            ResponsesRead = responses.Count,
            ObservationsDeleted = deleted,
            ObservationsWritten = written,
            ExclusionsPreserved = preserved,
            LearnersAffected = learners
        };
    }

    /// <summary>
    /// Everything the projector needs about one response, flattened into one read.
    /// </summary>
    private sealed record Pending(
        Guid ResponseId, long Sequence, Guid LearnerId, Guid ItemId, Guid ItemVersionId,
        Guid? ChoiceId, bool IsCorrect, bool Answered, bool IsFirstEncounter, int HintsUsed,
        bool RetryPermitted, bool WasAided, EvidenceDeliveryMode DeliveryMode, Guid? GroupId,
        int? ElapsedMs, Guid ContractVersionId, Guid? NodeId, DateTime OccurredAtUtc,
        EvidenceStrength BankCeiling);

    private async Task<ObservationProjectionReport> ProjectAsync(
        Guid? learnerId, int maxResponses, bool advanceWatermark, CancellationToken cancellationToken)
    {
        var checkpoint = await ResolveCheckpointAsync(cancellationToken);
        var floor = advanceWatermark ? checkpoint.Watermark : 0L;

        var query = _dbContext.LearnerResponses.AsNoTracking().Where(r => r.Sequence > floor);

        if (learnerId is { } id)
            query = query.Where(r => r.LearnerId == id);

        // The anti-join, not the watermark, is what makes this safe to run twice. The watermark
        // only bounds how far back the global sweep looks; correctness comes from never writing an
        // observation for a response that already has one.
        var pending = await query
            .Where(r => !_dbContext.Observations.Any(o => o.LearnerResponseId == r.Id))
            .OrderBy(r => r.Sequence)
            .Take(maxResponses)
            .Select(r => new Pending(
                r.Id, r.Sequence, r.LearnerId, r.ItemId, r.ItemVersionId,
                r.ChoiceId, r.IsCorrect, r.ChoiceId != null, r.IsFirstEncounter, r.HintsUsed,
                r.RetryPermitted, r.WasAided, r.DeliveryMode, r.GroupId, r.ElapsedMs,
                r.EvidenceContractVersionId, r.NodeId, r.OccurredAtUtc,
                r.Item!.Bank!.MaxEvidenceStrength))
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return new ObservationProjectionReport { Watermark = checkpoint.Watermark };
        }

        var contracts = await LoadContractsAsync(pending, cancellationToken);
        var mappings = await LoadTargetMappingsAsync(pending, cancellationToken);
        var statistics = await LoadStatisticsAsync(pending, cancellationToken);

        var written = 0;
        var unmapped = 0;
        var now = DateTime.UtcNow;

        foreach (var response in pending)
        {
            if (!mappings.TryGetValue(response.ItemId, out var targets) || targets.Count == 0)
            {
                // Content that no target claims. Counted rather than logged into the void: an item
                // mapped to nothing can never be measured, however many children answer it.
                unmapped++;
                continue;
            }

            var strength = StrengthFor(response, contracts);

            foreach (var mapping in targets)
            {
                _dbContext.Observations.Add(new Observation
                {
                    Id = Guid.NewGuid(),
                    LearnerResponseId = response.ResponseId,
                    LearnerId = response.LearnerId,
                    TargetId = mapping.TargetId,
                    ItemId = response.ItemId,
                    ItemVersionId = response.ItemVersionId,
                    Outcome = OutcomeFor(response),
                    Strength = strength,
                    Weight = mapping.Emphasis * WeightFor(response, contracts),
                    EvidenceContractVersionId = response.ContractVersionId,
                    GroupId = response.GroupId,
                    NodeId = response.NodeId,
                    ObservedAtUtc = response.OccurredAtUtc
                });

                written++;
            }

            Fold(statistics, response, now);
        }

        if (advanceWatermark)
        {
            checkpoint.Watermark = pending[^1].Sequence;
            checkpoint.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (unmapped > 0)
        {
            _logger.LogWarning(
                "{Count} responses produced no observation because their item maps to no learning target.",
                unmapped);
        }

        return new ObservationProjectionReport
        {
            ResponsesRead = pending.Count,
            ObservationsWritten = written,
            UnmappedResponses = unmapped,
            ItemStatisticsUpdated = statistics.Count,
            Watermark = checkpoint.Watermark
        };
    }

    /// <summary>
    /// **Where the item bank's trust ceiling is applied**, and the only place it is.
    /// <para>
    /// The contract decides what the conditions were worth; the bank decides how far the content
    /// itself can be trusted. An unreviewed teacher-authored question administered under perfect
    /// exam conditions still produces practice-class evidence, because the problem is not how it
    /// was given — it is that nobody qualified has checked the key. Derived here rather than stored
    /// on the response, so reviewing a bank re-grades its history on the next rebuild instead of
    /// leaving it stale forever.
    /// </para>
    /// </summary>
    private static EvidenceStrength StrengthFor(
        Pending response, IReadOnlyDictionary<Guid, EvidenceContractVersion> contracts)
    {
        if (!contracts.TryGetValue(response.ContractVersionId, out var contract))
            return EvidenceStrength.None;

        var strength = contract.StrengthFor(
            response.IsFirstEncounter, response.HintsUsed, response.RetryPermitted,
            response.DeliveryMode, response.WasAided);

        return strength <= response.BankCeiling ? strength : response.BankCeiling;
    }

    private static decimal WeightFor(
        Pending response, IReadOnlyDictionary<Guid, EvidenceContractVersion> contracts) =>
        contracts.TryGetValue(response.ContractVersionId, out var contract) ? contract.Weight : 0m;

    /// <summary>
    /// An unreached item is <see cref="ObservationOutcome.NoResponse"/>, **not incorrect**. A child
    /// who ran out of road in a runner did not get the question wrong; treating the two the same
    /// would let gameplay difficulty masquerade as not knowing the answer, which is precisely the
    /// contamination the evidence contract exists to prevent.
    /// </summary>
    private static ObservationOutcome OutcomeFor(Pending response) =>
        !response.Answered ? ObservationOutcome.NoResponse
        : response.IsCorrect ? ObservationOutcome.Correct
        : ObservationOutcome.Incorrect;

    // ---- running aggregates ------------------------------------------------------------------

    /// <summary>
    /// Folds one response into its item's statistics. **Incremental, never recomputed from raw** —
    /// see <see cref="ItemStatistics"/> for why that is a correctness requirement rather than a
    /// performance one.
    /// </summary>
    private static void Fold(
        Dictionary<Guid, ItemStatistics> statistics, Pending response, DateTime now)
    {
        if (!statistics.TryGetValue(response.ItemVersionId, out var stats)) return;

        // An unreached item tells us nothing about the item. Counting it would make every question
        // late in a lesson look harder than the ones before it, which is a statement about how far
        // children get in a runner, not about the mathematics.
        if (!response.Answered) return;

        stats.NTotal++;
        if (response.IsCorrect) stats.NCorrect++;

        if (response.IsFirstEncounter && response.HintsUsed == 0 && !response.RetryPermitted)
        {
            stats.NFirstEncounter++;
            if (response.IsCorrect) stats.NFirstEncounterCorrect++;
        }

        if (response.ElapsedMs is { } elapsed)
        {
            stats.SumElapsedMs += elapsed;
            stats.NElapsed++;
        }

        if (response.ChoiceId is { } choiceId)
            stats.ChoiceFrequency = Increment(stats.ChoiceFrequency, choiceId);

        stats.LastObservationSequence = Math.Max(stats.LastObservationSequence, response.Sequence);
        stats.UpdatedAtUtc = now;
    }

    /// <summary>
    /// Bumps one choice's count in the stored JSON. Small enough to rewrite whole: a distractor
    /// breakdown is at most a handful of keys, read whole and written whole.
    /// </summary>
    private static string Increment(string? json, Guid choiceId)
    {
        var counts = string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];

        var key = choiceId.ToString("D");
        counts[key] = counts.GetValueOrDefault(key) + 1;

        return JsonSerializer.Serialize(counts);
    }

    // ---- loads -------------------------------------------------------------------------------

    private async Task<Dictionary<Guid, EvidenceContractVersion>> LoadContractsAsync(
        List<Pending> pending, CancellationToken cancellationToken)
    {
        var ids = pending.Select(p => p.ContractVersionId).Distinct().ToList();

        return await _dbContext.EvidenceContractVersions
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);
    }

    private sealed record TargetMapping(Guid TargetId, decimal Emphasis);

    private async Task<Dictionary<Guid, List<TargetMapping>>> LoadTargetMappingsAsync(
        List<Pending> pending, CancellationToken cancellationToken)
    {
        var itemIds = pending.Select(p => p.ItemId).Distinct().ToList();

        var rows = await _dbContext.ItemTargetMappings
            .AsNoTracking()
            .Where(m => itemIds.Contains(m.ItemId))
            .Where(m => m.Target!.RetiredAtUtc == null)
            .Select(m => new { m.ItemId, m.TargetId, m.Emphasis })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new TargetMapping(r.TargetId, r.Emphasis)).ToList());
    }

    /// <summary>
    /// Loads (and creates on first sight) the global statistics row for every item version in this
    /// batch, tracked so the folds above are ordinary property writes.
    /// </summary>
    private async Task<Dictionary<Guid, ItemStatistics>> LoadStatisticsAsync(
        List<Pending> pending, CancellationToken cancellationToken)
    {
        var versionIds = pending.Select(p => p.ItemVersionId).Distinct().ToList();

        var existing = await _dbContext.ItemStatistics
            .Where(s => versionIds.Contains(s.ItemVersionId)
                        && s.Population == ItemStatisticsPopulations.Global)
            .ToDictionaryAsync(s => s.ItemVersionId, cancellationToken);

        foreach (var response in pending)
        {
            if (existing.ContainsKey(response.ItemVersionId)) continue;

            var stats = new ItemStatistics
            {
                ItemVersionId = response.ItemVersionId,
                Population = ItemStatisticsPopulations.Global,
                ItemId = response.ItemId,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _dbContext.ItemStatistics.Add(stats);
            existing[response.ItemVersionId] = stats;
        }

        return existing;
    }

    private async Task<ProjectionCheckpoint> ResolveCheckpointAsync(CancellationToken cancellationToken)
    {
        // Anything this context is already tracking wins, and that includes a row added by an
        // earlier call that has not been saved yet.
        //
        // Without this the second projection in one scope throws: the query below goes to the
        // database, does not find the unsaved row, and adds a second entity with the same key. It
        // only bites where the checkpoint does not exist yet — a fresh database, or a test — which
        // is why it survived until a caller projected several learners in one request. Cheap to
        // get right, and an identity conflict here fails the whole read for an unrelated reason.
        var tracked = _dbContext.ProjectionCheckpoints.Local
            .FirstOrDefault(c => c.Consumer == ProjectionConsumers.Observations);

        if (tracked is not null) return tracked;

        var checkpoint = await _dbContext.ProjectionCheckpoints
            .FirstOrDefaultAsync(c => c.Consumer == ProjectionConsumers.Observations, cancellationToken);

        if (checkpoint is not null) return checkpoint;

        checkpoint = new ProjectionCheckpoint
        {
            Consumer = ProjectionConsumers.Observations,
            Watermark = 0,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.ProjectionCheckpoints.Add(checkpoint);
        return checkpoint;
    }
}
