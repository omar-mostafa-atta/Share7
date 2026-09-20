using Microsoft.EntityFrameworkCore;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Assessment.Models;
using Share7.Application.Measurement.Interfaces;
using Share7.Domain.Assessment;
using Share7.Domain.Evidence;
using Share7.Domain.Measurement;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Assessment;

/// <inheritdoc cref="IExamCoverageService"/>
public class ExamCoverageService : IExamCoverageService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IObservationProjector _projector;
    private readonly IMeasurementService _measurements;

    /// <summary>
    /// Matched pairs of (what we said, what happened) needed before output C renders at all.
    /// <para>
    /// A stated bar rather than a silent one, so "we cannot predict your score" comes with a
    /// distance attached. Two hundred is the low end of what §6.5 calls a few hundred; below it a
    /// fitted band is an overfitted band, and an overfitted band about a child's examination
    /// prospects is the specific harm this whole section exists to avoid.
    /// </para>
    /// </summary>
    public const int MinimumCalibrationSample = 200;

    public ExamCoverageService(
        ApplicationDbContext dbContext,
        IObservationProjector projector,
        IMeasurementService measurements)
    {
        _dbContext = dbContext;
        _projector = projector;
        _measurements = measurements;
    }

    // ---------------------------------------------------------------------------- listing

    public async Task<IReadOnlyList<ExamSpecificationSummaryDto>> ListAsync(
        bool includeUnpublished = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ExamSpecificationVersions
            .AsNoTracking()
            .Where(v => v.RetiredAtUtc == null);

        if (!includeUnpublished) query = query.Where(v => v.PublishedAtUtc != null);

        return await query
            .Select(v => new ExamSpecificationSummaryDto
            {
                ExamSpecificationVersionId = v.Id,
                SpecificationKey = v.ExamSpecification!.SpecificationKey,
                Name = v.ExamSpecification.Name,
                VersionLabel = v.VersionLabel,
                AuthorityName = v.ExamSpecification.Authority!.Name,
                SubjectLabel = v.ExamSpecification.SubjectLabel,
                SittingDate = v.SittingDate,
                IsPublished = v.PublishedAtUtc != null,
                TargetCount = v.Blueprint!.Areas.SelectMany(a => a.Lines).Count(),
                PlaceholderTargetCount = v.Blueprint.Areas
                    .SelectMany(a => a.Lines)
                    .Count(l => l.Target!.IsPlaceholder),
                ReportedOutcomes = v.ReportedOutcomes.Count,
                CalibrationUsableOutcomes = v.ReportedOutcomes.Count(
                    o => o.ConsentedToCalibrationUse
                         && o.ConsentWithdrawnAtUtc == null
                         && o.Verification != OutcomeVerification.Disputed)
            })
            .OrderBy(v => v.Name)
            .ThenBy(v => v.VersionLabel)
            .ToListAsync(cancellationToken);
    }

    // ------------------------------------------------------------------------- projection

    public async Task<ExamProjectionDto?> GetProjectionAsync(
        Guid learnerId, Guid examSpecificationVersionId, Guid langId,
        CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.ExamSpecificationVersions
            .AsNoTracking()
            .Where(v => v.Id == examSpecificationVersionId)
            .Select(v => new
            {
                v.Id,
                v.BlueprintId,
                v.VersionLabel,
                v.SittingDate,
                ExamName = v.ExamSpecification!.Name,
                AuthorityName = v.ExamSpecification.Authority != null
                    ? v.ExamSpecification.Authority.Name
                    : "Unattributed"
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null) return null;

        var calibration = await CalibrationSampleSizeAsync(version.Id, cancellationToken);

        return await ComputeAsync(
            learnerId, version.BlueprintId, langId,
            version.Id, version.ExamName, version.VersionLabel, version.AuthorityName,
            version.SittingDate, calibration, cancellationToken);
    }

    public async Task<ExamProjectionDto?> GetBlueprintCoverageAsync(
        Guid learnerId, Guid blueprintId, Guid langId, CancellationToken cancellationToken = default)
    {
        var name = await _dbContext.AssessmentBlueprints
            .AsNoTracking()
            .Where(b => b.Id == blueprintId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (name is null) return null;

        // No examination wrapped around it, so no calibration and no sitting date. Coverage and
        // proficiency are exactly as meaningful; only output C is unavailable, and it was going to
        // be anyway.
        return await ComputeAsync(
            learnerId, blueprintId, langId,
            examVersionId: null, name, versionLabel: "blueprint", authorityName: "Share7",
            sittingDate: null, calibrationSample: 0, cancellationToken);
    }

    // ----------------------------------------------------------------- the computation

    /// <summary>One blueprint line, resolved and weighted, before any evidence is looked at.</summary>
    private sealed record Line(
        Guid LineId, Guid TargetId, string Statement, bool IsPlaceholder,
        string AreaKey, string AreaLabel, decimal AreaWeight, decimal LineWeight, int AreaOrder);

    /// <summary>What one learner's evidence says about one target, for this blueprint's purposes.</summary>
    private sealed record Evidence(
        int Admitted, int Correct, int ExamLike, int Practice, DateTime? MedianObservedAtUtc);

    private async Task<ExamProjectionDto?> ComputeAsync(
        Guid learnerId, Guid blueprintId, Guid langId,
        Guid? examVersionId, string examName, string versionLabel, string authorityName,
        DateOnly? sittingDate, int calibrationSample, CancellationToken cancellationToken)
    {
        var blueprint = await _dbContext.AssessmentBlueprints
            .AsNoTracking()
            .Include(b => b.Areas).ThenInclude(a => a.Lines)
            .FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken);

        if (blueprint is null) return null;

        // What the learner just did has to be in the answer. A readiness screen that lags a lesson
        // is a readiness screen nobody believes twice.
        await _projector.ProjectForLearnerAsync(learnerId, cancellationToken);
        await _measurements.RecomputeForLearnerAsync(learnerId, cancellationToken);

        var lines = await ResolveLinesAsync(blueprint, langId, cancellationToken);

        if (lines.Count == 0)
        {
            return Empty(blueprint, examVersionId, examName, versionLabel, authorityName, sittingDate);
        }

        var evidence = await LoadEvidenceAsync(
            learnerId, lines.Select(l => l.TargetId).Distinct().ToList(),
            blueprint.RequiredStrength, cancellationToken);

        // The per-line sufficiency bar is the platform's own gate for saying anything about a
        // target, reused deliberately: "you have exam-quality evidence on 62% of this paper" then
        // means "62% of it is made of claims we could actually speak to", rather than meaning
        // whatever a second, unrelated threshold happened to be set to.
        var required = await RequiredObservationsPerLineAsync(cancellationToken);

        var estimates = await LoadEstimatesAsync(
            learnerId, lines.Select(l => l.TargetId).Distinct().ToList(), cancellationToken);

        var projection = Assemble(
            blueprint, lines, evidence, estimates, required,
            examVersionId, examName, versionLabel, authorityName, sittingDate,
            calibrationSample, await SuggestedNodesAsync(lines, langId, cancellationToken));

        // Stored only when there is an examination to store it against. A bare blueprint read is a
        // teacher checking their own unit test, not a claim about a learner and a paper, and
        // writing a row for it would put readiness figures in the table for things nobody sits.
        if (examVersionId is { } versionId)
            await PersistAsync(learnerId, versionId, projection, cancellationToken);

        return projection;
    }

    /// <summary>
    /// Writes the computed projection back as the learner's live row, so a report over a class of
    /// thirty is thirty reads rather than thirty recomputations.
    /// <para>
    /// Replace rather than merge: the gaps are a ranked list whose membership changes completely
    /// between computations, and reconciling them row by row would cost more than rewriting them.
    /// </para>
    /// </summary>
    private async Task PersistAsync(
        Guid learnerId, Guid versionId, ExamProjectionDto dto, CancellationToken cancellationToken)
    {
        var row = await _dbContext.ExamProjections
            .Include(p => p.Gaps)
            .FirstOrDefaultAsync(
                p => p.LearnerId == learnerId
                     && p.ExamSpecificationVersionId == versionId
                     && p.MethodKey == ProjectionMethods.CoverageV1,
                cancellationToken);

        if (row is null)
        {
            row = new ExamProjection
            {
                Id = Guid.NewGuid(),
                LearnerId = learnerId,
                ExamSpecificationVersionId = versionId,
                MethodKey = ProjectionMethods.CoverageV1
            };

            _dbContext.ExamProjections.Add(row);
        }
        else
        {
            _dbContext.ExamProjectionGaps.RemoveRange(row.Gaps);
        }

        row.Sufficiency = dto.Sufficiency;
        row.CoverageRatio = dto.CoverageRatio;
        row.WeakestAreaCoverage = dto.WeakestAreaCoverage;
        row.BasisObservationCount = dto.BasisObservationCount;
        row.ExamLikeObservationCount = dto.ExamLikeObservationCount;
        row.MedianEvidenceAgeDays = dto.MedianEvidenceAgeDays;
        row.ProficiencyBandLow = dto.ProficiencyBandLow;
        row.ProficiencyBandHigh = dto.ProficiencyBandHigh;
        row.Confidence = dto.Confidence;
        row.OutcomeBandLow = dto.OutcomeBandLow;
        row.OutcomeBandHigh = dto.OutcomeBandHigh;
        row.CalibrationSampleSize = dto.CalibrationSampleSize;
        row.ComputedAtUtc = dto.ComputedAtUtc;

        // The top of the list only. The whole ranked set is recomputed on every read anyway, and a
        // stored tail nobody shows is a table that grows without being looked at.
        foreach (var gap in dto.Gaps.Take(20))
        {
            _dbContext.ExamProjectionGaps.Add(new ExamProjectionGap
            {
                Id = Guid.NewGuid(),
                ExamProjectionId = row.Id,
                AreaKey = gap.AreaKey,
                AreaLabel = gap.AreaLabel,
                TargetId = gap.TargetId,
                Kind = gap.Kind,
                WeightInExam = gap.WeightInExam,
                ObservationCount = gap.ObservationCount,
                ExamLikeObservationCount = gap.ExamLikeObservationCount,
                ObservationsNeeded = gap.ObservationsNeeded,
                SuggestedNodeId = gap.SuggestedNodeId,
                Rank = gap.Rank
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Flattens the blueprint into weighted lines, normalising area weights across the blueprint
    /// and line weights within each area — so an author can write marks and the arithmetic still
    /// works in shares.
    /// </summary>
    private async Task<List<Line>> ResolveLinesAsync(
        AssessmentBlueprint blueprint, Guid langId, CancellationToken cancellationToken)
    {
        var targetIds = blueprint.Areas.SelectMany(a => a.Lines).Select(l => l.TargetId).Distinct().ToList();

        var targets = await _dbContext.LearningTargets
            .AsNoTracking()
            .Where(t => targetIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.IsPlaceholder,
                Statement = t.Translations
                    .Where(x => x.LangId == langId)
                    .Select(x => x.Statement)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var areaTotal = blueprint.Areas.Sum(a => a.Weight);
        if (areaTotal <= 0) areaTotal = blueprint.Areas.Count;

        var lines = new List<Line>();

        foreach (var area in blueprint.Areas.OrderBy(a => a.Order))
        {
            var lineTotal = area.Lines.Sum(l => l.Weight);
            if (lineTotal <= 0) lineTotal = area.Lines.Count;
            if (lineTotal <= 0) continue;

            var areaWeight = area.Weight / areaTotal;

            foreach (var line in area.Lines)
            {
                targets.TryGetValue(line.TargetId, out var target);

                lines.Add(new Line(
                    line.Id, line.TargetId,
                    target?.Statement ?? string.Empty,
                    target?.IsPlaceholder ?? false,
                    area.AreaKey, area.Label, areaWeight,
                    line.Weight / lineTotal, area.Order));
            }
        }

        return lines;
    }

    /// <summary>
    /// One learner's admitted evidence per target.
    /// <para>
    /// The same four exclusions the measurement layer applies, for the same reasons: excluded rows,
    /// unreached items, anything below the blueprint's declared strength floor, and anything a
    /// group produced. <c>ExamLike</c> is counted separately from <c>Admitted</c> so that a
    /// formative blueprint admitting practice evidence can still report honestly how much of it
    /// would survive an exam-grade filter.
    /// </para>
    /// </summary>
    private async Task<Dictionary<Guid, Evidence>> LoadEvidenceAsync(
        Guid learnerId, List<Guid> targetIds, EvidenceStrength requiredStrength,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Observations
            .AsNoTracking()
            .Where(o => o.LearnerId == learnerId && targetIds.Contains(o.TargetId))
            .Where(o => o.ExcludedAtUtc == null)
            .Where(o => o.Outcome != ObservationOutcome.NoResponse)
            .Where(o => o.GroupId == null)
            .Select(o => new { o.TargetId, o.Outcome, o.Strength, o.ObservedAtUtc })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.TargetId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var admitted = g.Where(r => r.Strength >= requiredStrength).ToList();
                    var dates = admitted.Select(r => r.ObservedAtUtc).OrderBy(d => d).ToList();

                    return new Evidence(
                        admitted.Count,
                        admitted.Count(r => r.Outcome == ObservationOutcome.Correct),
                        g.Count(r => r.Strength == EvidenceStrength.Assessment),
                        g.Count(r => r.Strength == EvidenceStrength.Practice),
                        dates.Count == 0 ? null : dates[dates.Count / 2]);
                });
    }

    private sealed record Estimate(decimal Value, decimal Low, decimal High);

    private async Task<Dictionary<Guid, Estimate>> LoadEstimatesAsync(
        Guid learnerId, List<Guid> targetIds, CancellationToken cancellationToken) =>
        await _dbContext.Measurements
            .AsNoTracking()
            .Where(m => m.LearnerId == learnerId
                        && m.MethodKey == MeasurementMethods.ClassicalV1
                        && targetIds.Contains(m.TargetId))
            .ToDictionaryAsync(
                m => m.TargetId,
                m => new Estimate(m.Estimate, m.IntervalLow, m.IntervalHigh),
                cancellationToken);

    /// <summary>
    /// A node teaching each target, so a gap can be a button rather than a sentence. Null for a
    /// target nothing in the structure teaches — which is worth surfacing, because it means the
    /// learner cannot close that gap on this platform at all.
    /// </summary>
    private async Task<Dictionary<Guid, (Guid NodeId, string? Title)>> SuggestedNodesAsync(
        List<Line> lines, Guid langId, CancellationToken cancellationToken)
    {
        var targetIds = lines.Select(l => l.TargetId).Distinct().ToList();

        var rows = await _dbContext.NodeTargetMappings
            .AsNoTracking()
            .Where(m => targetIds.Contains(m.TargetId))
            .Select(m => new
            {
                m.TargetId,
                m.NodeId,
                Title = _dbContext.CurriculumNodeTranslations
                    .Where(t => t.NodeId == m.NodeId && t.LangId == langId)
                    .Select(t => t.Title)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.TargetId)
            .ToDictionary(g => g.Key, g => (g.First().NodeId, g.First().Title));
    }

    private async Task<int> RequiredObservationsPerLineAsync(CancellationToken cancellationToken) =>
        await _dbContext.MasteryRules
            .AsNoTracking()
            .Where(r => r.PublishedAtUtc != null && r.RetiredAtUtc == null)
            .OrderByDescending(r => r.VersionNumber)
            .Select(r => (int?)r.MinObservations)
            .FirstOrDefaultAsync(cancellationToken) ?? 8;

    private async Task<int> CalibrationSampleSizeAsync(
        Guid examVersionId, CancellationToken cancellationToken) =>
        await _dbContext.ReportedExamOutcomes
            .AsNoTracking()
            .CountAsync(
                o => o.ExamSpecificationVersionId == examVersionId
                     && o.ConsentedToCalibrationUse
                     && o.ConsentWithdrawnAtUtc == null
                     && o.Verification != OutcomeVerification.Disputed
                     && (o.ReportedScore != null || o.ReportedGrade != null),
                cancellationToken);

    // -------------------------------------------------------------------------- assembly

    private ExamProjectionDto Assemble(
        AssessmentBlueprint blueprint, List<Line> lines,
        Dictionary<Guid, Evidence> evidence, Dictionary<Guid, Estimate> estimates, int required,
        Guid? examVersionId, string examName, string versionLabel, string authorityName,
        DateOnly? sittingDate, int calibrationSample,
        Dictionary<Guid, (Guid NodeId, string? Title)> nodes)
    {
        var now = DateTime.UtcNow;

        var areas = new List<ExamAreaCoverageDto>();
        var gaps = new List<ExamGapDto>();

        decimal coverage = 0m;
        decimal weightedEstimateLow = 0m;
        decimal weightedEstimateHigh = 0m;
        decimal coveredWeight = 0m;

        var totalAdmitted = 0;
        var totalExamLike = 0;
        var ages = new List<int>();

        foreach (var group in lines.GroupBy(l => (l.AreaKey, l.AreaLabel, l.AreaWeight, l.AreaOrder))
                     .OrderBy(g => g.Key.AreaOrder))
        {
            decimal areaCoverage = 0m;
            decimal areaEstimate = 0m;
            decimal areaCoveredWeight = 0m;

            var areaAdmitted = 0;
            var areaExamLike = 0;
            var covered = 0;

            foreach (var line in group)
            {
                evidence.TryGetValue(line.TargetId, out var found);
                var e = found ?? new Evidence(0, 0, 0, 0, null);

                // **A placeholder contributes nothing, whatever evidence it carries.** A lesson
                // wearing a target's clothes cannot say what a paper examines, and letting it
                // count would turn a completion percentage into an exam-readiness claim — the
                // precise fabrication §20.5 exists to prevent.
                var admitted = line.IsPlaceholder ? 0 : e.Admitted;

                var lineCoverage = required <= 0
                    ? (admitted > 0 ? 1m : 0m)
                    : Math.Min(1m, (decimal)admitted / required);

                var weightInExam = line.AreaWeight * line.LineWeight;

                areaCoverage += line.LineWeight * lineCoverage;
                areaAdmitted += admitted;
                areaExamLike += line.IsPlaceholder ? 0 : e.ExamLike;
                totalAdmitted += admitted;
                totalExamLike += line.IsPlaceholder ? 0 : e.ExamLike;

                if (e.MedianObservedAtUtc is { } median && !line.IsPlaceholder)
                    ages.Add(Math.Max(0, (int)(now - median).TotalDays));

                if (lineCoverage >= 1m && estimates.TryGetValue(line.TargetId, out var estimate))
                {
                    covered++;
                    areaCoveredWeight += line.LineWeight;
                    coveredWeight += weightInExam;
                    areaEstimate += line.LineWeight * estimate.Value;
                    weightedEstimateLow += weightInExam * estimate.Low;
                    weightedEstimateHigh += weightInExam * estimate.High;
                }

                if (lineCoverage < 1m)
                {
                    gaps.Add(new ExamGapDto
                    {
                        AreaKey = line.AreaKey,
                        AreaLabel = line.AreaLabel,
                        TargetId = line.TargetId,
                        Statement = line.Statement,
                        Kind = KindFor(line, e, admitted, required, blueprint, now),
                        WeightInExam = weightInExam,
                        ObservationCount = e.Admitted,
                        ExamLikeObservationCount = e.ExamLike,
                        ObservationsNeeded = Math.Max(0, required - admitted),
                        SuggestedNodeId = nodes.TryGetValue(line.TargetId, out var node) ? node.NodeId : null,
                        SuggestedNodeTitle = nodes.TryGetValue(line.TargetId, out var n) ? n.Title : null,
                        Rank = 0
                    });
                }
            }

            coverage += group.Key.AreaWeight * areaCoverage;

            areas.Add(new ExamAreaCoverageDto
            {
                AreaKey = group.Key.AreaKey,
                Label = group.Key.AreaLabel,
                WeightInExam = group.Key.AreaWeight,
                Coverage = Round(areaCoverage),
                TargetCount = group.Count(),
                TargetsCovered = covered,
                ObservationCount = areaAdmitted,
                ExamLikeObservationCount = areaExamLike,

                // Never an estimate without its coverage beside it: an excellent score on a tenth
                // of a section is not an excellent section.
                Estimate = areaCoveredWeight > 0 ? Round(areaEstimate / areaCoveredWeight) : null
            });
        }

        // Worth-most first, so the list reads as advice rather than as a catalogue of failures.
        var ranked = gaps
            .OrderByDescending(g => g.WeightInExam)
            .ThenByDescending(g => g.ObservationsNeeded)
            .Select((g, i) => g with { Rank = i + 1 })
            .ToList();

        var weakest = areas.Count == 0 ? 0m : areas.Min(a => a.Coverage);
        var medianAge = ages.Count == 0 ? (int?)null : ages.OrderBy(a => a).ElementAt(ages.Count / 2);

        var sufficiency = Decide(
            blueprint, coverage, weakest, totalExamLike, areas, medianAge, calibrationSample);

        // Output B renders when the learner-side claims hold, which is both the sufficient case and
        // the uncalibrated one: §6.2's B needs conditions and coverage, not calibration. Only C
        // needs the sample, and C is null in both.
        var reportBands = sufficiency is ProjectionSufficiency.Sufficient or ProjectionSufficiency.Uncalibrated
                          && coveredWeight > 0;

        return new ExamProjectionDto
        {
            ExamSpecificationVersionId = examVersionId ?? blueprint.Id,
            ExamName = examName,
            VersionLabel = versionLabel,
            AuthorityName = authorityName,
            SourceNote = blueprint.SourceNote,
            SittingDate = sittingDate,

            Sufficiency = sufficiency,

            CoverageRatio = Round(coverage),
            WeakestAreaCoverage = Round(weakest),
            Areas = areas,
            Gaps = ranked,

            ProficiencyBandLow = reportBands ? Round(weightedEstimateLow / coveredWeight) : null,
            ProficiencyBandHigh = reportBands ? Round(weightedEstimateHigh / coveredWeight) : null,
            Confidence = reportBands
                ? ConfidenceFor(totalExamLike, weightedEstimateLow, weightedEstimateHigh, coveredWeight)
                : null,

            // **Nothing here can fill these by reasoning**, and there is no branch that tries.
            OutcomeBandLow = null,
            OutcomeBandHigh = null,
            CalibrationSampleSize = calibrationSample > 0 ? calibrationSample : null,

            BasisObservationCount = totalAdmitted,
            ExamLikeObservationCount = totalExamLike,
            MedianEvidenceAgeDays = medianAge,

            Thresholds = new ExamThresholdsDto
            {
                MinCoverageRatio = blueprint.MinCoverageRatio,
                MinAreaCoverageRatio = blueprint.MinAreaCoverageRatio,
                MinObservationsOverall = blueprint.MinObservationsOverall,
                MinObservationsPerArea = blueprint.MinObservationsPerArea,
                MaxMedianEvidenceAgeDays = blueprint.MaxMedianEvidenceAgeDays
            },

            MethodKey = ProjectionMethods.CoverageV1,
            ComputedAtUtc = now
        };
    }

    /// <summary>
    /// The gates, in the order that produces the most useful message.
    /// <para>
    /// Coverage first because a blind spot is the most actionable thing to be told; then
    /// conditions, because "you have done the work but not under exam conditions" is a different
    /// and equally actionable sentence; then recency. Only when all three hold does the answer turn
    /// on whether a calibration exists — and today, for every exam, it does not.
    /// </para>
    /// </summary>
    private static ProjectionSufficiency Decide(
        AssessmentBlueprint blueprint, decimal coverage, decimal weakestArea, int examLike,
        List<ExamAreaCoverageDto> areas, int? medianAge, int calibrationSample)
    {
        if (coverage < blueprint.MinCoverageRatio) return ProjectionSufficiency.InsufficientCoverage;

        // The condition that matters, and the reason an average is not enough on its own: 0.75
        // overall with nothing at all on statistics is a learner walking into a paper unwarned.
        if (weakestArea < blueprint.MinAreaCoverageRatio) return ProjectionSufficiency.InsufficientCoverage;

        if (examLike < blueprint.MinObservationsOverall) return ProjectionSufficiency.InsufficientConditions;

        if (areas.Any(a => a.ExamLikeObservationCount < blueprint.MinObservationsPerArea))
            return ProjectionSufficiency.InsufficientConditions;

        if (medianAge is { } age && age > blueprint.MaxMedianEvidenceAgeDays)
            return ProjectionSufficiency.InsufficientRecency;

        return calibrationSample >= MinimumCalibrationSample
            ? ProjectionSufficiency.Sufficient
            : ProjectionSufficiency.Uncalibrated;
    }

    /// <summary>
    /// Which of four things is wrong with one line's evidence. <see cref="CoverageGapKind.PracticeOnly"/>
    /// is the interesting one: the learner has done the work and it does not count yet, which is
    /// advice rather than a judgement.
    /// </summary>
    private static CoverageGapKind KindFor(
        Line line, Evidence e, int admitted, int required, AssessmentBlueprint blueprint, DateTime now)
    {
        if (admitted == 0 && e.Practice > 0) return CoverageGapKind.PracticeOnly;
        if (admitted == 0) return CoverageGapKind.NoEvidence;

        if (e.MedianObservedAtUtc is { } median
            && (now - median).TotalDays > blueprint.MaxMedianEvidenceAgeDays)
            return CoverageGapKind.StaleEvidence;

        return CoverageGapKind.ThinEvidence;
    }

    /// <summary>
    /// A word for how much to trust the band, from how much exam-like evidence there is and how
    /// wide the band came out. Never a number — a percentage on a confidence is a second false
    /// precision stacked on the first (§6.4).
    /// </summary>
    private static ConfidenceLabel ConfidenceFor(
        int examLike, decimal low, decimal high, decimal coveredWeight)
    {
        var width = coveredWeight > 0 ? (high - low) / coveredWeight : 1m;

        if (examLike >= 60 && width <= 0.20m) return ConfidenceLabel.High;
        if (examLike >= 20 && width <= 0.35m) return ConfidenceLabel.Moderate;

        return ConfidenceLabel.Low;
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>A blueprint with no lines covers nothing, and says so rather than dividing by zero.</summary>
    private static ExamProjectionDto Empty(
        AssessmentBlueprint blueprint, Guid? examVersionId, string examName,
        string versionLabel, string authorityName, DateOnly? sittingDate) =>
        new()
        {
            ExamSpecificationVersionId = examVersionId ?? blueprint.Id,
            ExamName = examName,
            VersionLabel = versionLabel,
            AuthorityName = authorityName,
            SourceNote = blueprint.SourceNote,
            SittingDate = sittingDate,
            Sufficiency = ProjectionSufficiency.InsufficientCoverage,
            CoverageRatio = 0m,
            WeakestAreaCoverage = 0m,
            Areas = [],
            Gaps = [],
            BasisObservationCount = 0,
            ExamLikeObservationCount = 0,
            Thresholds = new ExamThresholdsDto
            {
                MinCoverageRatio = blueprint.MinCoverageRatio,
                MinAreaCoverageRatio = blueprint.MinAreaCoverageRatio,
                MinObservationsOverall = blueprint.MinObservationsOverall,
                MinObservationsPerArea = blueprint.MinObservationsPerArea,
                MaxMedianEvidenceAgeDays = blueprint.MaxMedianEvidenceAgeDays
            },
            MethodKey = ProjectionMethods.CoverageV1,
            ComputedAtUtc = DateTime.UtcNow
        };
}
