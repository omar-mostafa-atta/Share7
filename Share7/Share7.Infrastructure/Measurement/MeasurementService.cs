using Microsoft.EntityFrameworkCore;
using Share7.Application.Measurement.Interfaces;
using Share7.Application.Measurement.Models;
using Share7.Domain.Evidence;
using Share7.Domain.Measurement;

// Aliased because this file's own namespace is Share7.Infrastructure.Measurement, so an
// unqualified `Measurement` binds to the namespace rather than to the entity.
using MeasurementEntity = Share7.Domain.Measurement.Measurement;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Measurement;

/// <inheritdoc cref="IMeasurementService"/>
public class MeasurementService : IMeasurementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IObservationProjector _projector;

    public MeasurementService(ApplicationDbContext dbContext, IObservationProjector projector)
    {
        _dbContext = dbContext;
        _projector = projector;
    }

    /// <summary>What one target's admitted observations add up to, before any judgement is applied.</summary>
    private sealed record Tally(
        Guid TargetId, int Count, int Correct, int AssessmentCount, long LastSequence, DateTime LastObservedAtUtc);

    public async Task<int> RecomputeForLearnerAsync(
        Guid learnerId, CancellationToken cancellationToken = default)
    {
        var rule = await ActiveRuleAsync(cancellationToken);
        if (rule is null) return 0;

        var tallies = await TallyAsync(learnerId, rule.MinStrength, cancellationToken);

        var measurements = await _dbContext.Measurements
            .Where(m => m.LearnerId == learnerId && m.MethodKey == MeasurementMethods.ClassicalV1)
            .ToDictionaryAsync(m => m.TargetId, cancellationToken);

        var verdicts = await _dbContext.MasteryVerdicts
            .Where(v => v.LearnerId == learnerId)
            .ToDictionaryAsync(v => v.TargetId, cancellationToken);

        var now = DateTime.UtcNow;
        var touched = 0;

        foreach (var tally in tallies)
        {
            var (estimate, low, high) = WilsonInterval.ForStorage(tally.Correct, tally.Count);

            if (!measurements.TryGetValue(tally.TargetId, out var measurement))
            {
                measurement = new MeasurementEntity
                {
                    Id = Guid.NewGuid(),
                    LearnerId = learnerId,
                    TargetId = tally.TargetId,
                    MethodKey = MeasurementMethods.ClassicalV1
                };
                _dbContext.Measurements.Add(measurement);
                measurements[tally.TargetId] = measurement;
            }

            measurement.Estimate = estimate;
            measurement.IntervalLow = low;
            measurement.IntervalHigh = high;
            measurement.ObservationCount = tally.Count;
            measurement.AssessmentCount = tally.AssessmentCount;
            measurement.CorrectCount = tally.Correct;
            measurement.LastObservationSequence = tally.LastSequence;
            measurement.ComputedAtUtc = now;

            var state = rule.Decide(tally.Count, estimate, low);

            if (!verdicts.TryGetValue(tally.TargetId, out var verdict))
            {
                verdict = new MasteryVerdict
                {
                    Id = Guid.NewGuid(),
                    LearnerId = learnerId,
                    TargetId = tally.TargetId
                };
                _dbContext.MasteryVerdicts.Add(verdict);
                verdicts[tally.TargetId] = verdict;
            }

            verdict.MasteryRuleId = rule.Id;
            verdict.MeasurementId = measurement.Id;
            verdict.State = state;
            verdict.AdmittedObservations = tally.Count;
            verdict.DecidedAtUtc = now;

            touched++;
        }

        // A target whose observations were all excluded — a mis-keyed item withdrawn, say — must
        // stop claiming what it claimed. Zeroed rather than deleted, so the row's history of having
        // been measured survives the correction.
        var live = tallies.Select(t => t.TargetId).ToHashSet();

        foreach (var (targetId, measurement) in measurements)
        {
            if (live.Contains(targetId) || measurement.ObservationCount == 0) continue;

            measurement.Estimate = 0m;
            measurement.IntervalLow = 0m;
            measurement.IntervalHigh = 1m;
            measurement.ObservationCount = 0;
            measurement.AssessmentCount = 0;
            measurement.CorrectCount = 0;
            measurement.ComputedAtUtc = now;

            if (verdicts.TryGetValue(targetId, out var verdict))
            {
                verdict.State = MasteryState.Insufficient;
                verdict.AdmittedObservations = 0;
                verdict.DecidedAtUtc = now;
            }

            touched++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return touched;
    }

    /// <summary>
    /// Sums one learner's admitted observations per target.
    /// <para>
    /// Four exclusions, each load-bearing:
    /// excluded rows (a human said this does not count);
    /// <see cref="ObservationOutcome.NoResponse"/> (never reaching a question is not getting it
    /// wrong — counting it would let gameplay difficulty read as not knowing the answer);
    /// anything below the rule's strength floor;
    /// and anything carrying a <c>GroupId</c>, because a group answer is not one child's answer.
    /// </para>
    /// </summary>
    private async Task<List<Tally>> TallyAsync(
        Guid learnerId, EvidenceStrength minStrength, CancellationToken cancellationToken) =>
        await _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.LearnerId == learnerId)
            .Where(o => o.ExcludedAtUtc == null)
            .Where(o => o.Outcome != ObservationOutcome.NoResponse)
            .Where(o => o.Strength >= minStrength)
            .Where(o => o.GroupId == null)
            .GroupBy(o => o.TargetId)
            .Select(g => new Tally(
                g.Key,
                g.Count(),
                g.Count(o => o.Outcome == ObservationOutcome.Correct),
                g.Count(o => o.Strength == EvidenceStrength.Assessment),
                g.Max(o => o.Sequence),
                g.Max(o => o.ObservedAtUtc)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TargetMeasurementDto>> GetForLearnerAsync(
        Guid learnerId, Guid langId, Guid? nodeId = null, CancellationToken cancellationToken = default)
    {
        // What the learner just did has to be in the answer, or the screen contradicts the lesson
        // they finished ten seconds ago.
        await _projector.ProjectForLearnerAsync(learnerId, cancellationToken);
        await RecomputeForLearnerAsync(learnerId, cancellationToken);

        var rule = await ActiveRuleAsync(cancellationToken);

        var query = _dbContext.Measurements
            .AsNoTracking()
            .Where(m => m.LearnerId == learnerId && m.MethodKey == MeasurementMethods.ClassicalV1);

        if (nodeId is { } node)
        {
            // Scoped to the targets this node teaches, so a lesson screen answers about that lesson.
            query = query.Where(m => _dbContext.NodeTargetMappings
                .Any(n => n.NodeId == node && n.TargetId == m.TargetId));
        }

        var rows = await query
            .Select(m => new
            {
                m.TargetId,
                m.Target!.IsPlaceholder,
                m.Estimate,
                m.IntervalLow,
                m.IntervalHigh,
                m.ObservationCount,
                m.AssessmentCount,
                m.ComputedAtUtc,
                Statement = m.Target.Translations
                    .Where(t => t.LangId == langId)
                    .Select(t => t.Statement)
                    .FirstOrDefault(),
                State = _dbContext.MasteryVerdicts
                    .Where(v => v.LearnerId == learnerId && v.TargetId == m.TargetId)
                    .Select(v => (MasteryState?)v.State)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var minimum = rule?.MinObservations ?? 0;

        return
        [
            .. rows.Select(r =>
            {
                var state = r.State ?? MasteryState.Insufficient;
                var reportable = state != MasteryState.Insufficient;

                return new TargetMeasurementDto
                {
                    TargetId = r.TargetId,
                    Statement = r.Statement ?? string.Empty,
                    IsPlaceholder = r.IsPlaceholder,
                    State = state,

                    // **Null, not zero, below the gate.** The whole point of the insufficient state
                    // is that no number is reported with it; emitting 0.0 here would hand every
                    // client a figure to render and every parent a score their child never got.
                    Estimate = reportable ? r.Estimate : null,
                    IntervalLow = reportable ? r.IntervalLow : null,
                    IntervalHigh = reportable ? r.IntervalHigh : null,

                    ObservationCount = r.ObservationCount,
                    AssessmentCount = r.AssessmentCount,
                    ObservationsUntilReportable = Math.Max(0, minimum - r.ObservationCount),
                    LastObservedAtUtc = r.ComputedAtUtc,
                    RuleKey = rule?.RuleKey,
                    RuleVersion = rule?.VersionNumber
                };
            })
            .OrderByDescending(d => d.ObservationCount)
            .ThenBy(d => d.Statement)
        ];
    }

    /// <summary>
    /// The published rule with the highest version. Null when none is published, which is a
    /// deployment fault rather than a state to paper over — no rule means no verdict, and a verdict
    /// invented without one would be exactly the unexplainable judgement this design refuses.
    /// </summary>
    private Task<MasteryRule?> ActiveRuleAsync(CancellationToken cancellationToken) =>
        _dbContext.MasteryRules
            .AsNoTracking()
            .Where(r => r.PublishedAtUtc != null && r.RetiredAtUtc == null)
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
}
