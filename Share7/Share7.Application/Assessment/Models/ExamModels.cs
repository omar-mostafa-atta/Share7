using Share7.Domain.Assessment;

namespace Share7.Application.Assessment.Models;

/// <summary>
/// What Share7 will say about one learner and one examination.
/// <para>
/// **Every band field is nullable and <see cref="Sufficiency"/> is not.** That asymmetry is the
/// §14.3 rule put in the type system: the insufficient case is a normal, well-formed, useful
/// response carrying <see cref="Gaps"/>, not an error and not a zero, and there is no shape of this
/// record in which a client receives a number without the verdict that says whether it may be
/// shown.
/// </para>
/// <para>
/// There is also **no field capable of carrying a point prediction**, which is deliberate: the
/// cheapest way to be certain nobody renders "you will get 61%" to a fifteen-year-old is to give
/// the server nowhere to put it (§6.4).
/// </para>
/// </summary>
public sealed record ExamProjectionDto
{
    public required Guid ExamSpecificationVersionId { get; init; }
    public required string ExamName { get; init; }
    public required string VersionLabel { get; init; }

    /// <summary>Who sets the paper. Shown, because a Share7 benchmark is not a ministry's exam.</summary>
    public required string AuthorityName { get; init; }

    /// <summary>Where the blueprint came from. Read this before trusting the number above it.</summary>
    public string? SourceNote { get; init; }

    public DateOnly? SittingDate { get; init; }

    public required ProjectionSufficiency Sufficiency { get; init; }

    // ------------------------------------------------------------------- output A: coverage

    /// <summary>
    /// The share of the paper the learner has admissible evidence on. **Always present** — it is
    /// arithmetic, it needs no calibration, and it is the day-one product.
    /// </summary>
    public required decimal CoverageRatio { get; init; }

    public required decimal WeakestAreaCoverage { get; init; }
    public required IReadOnlyList<ExamAreaCoverageDto> Areas { get; init; }

    /// <summary>What is missing, worth-most first. The payload that makes an insufficient answer useful.</summary>
    public required IReadOnlyList<ExamGapDto> Gaps { get; init; }

    // ----------------------------------------------------------------- output B: proficiency

    public decimal? ProficiencyBandLow { get; init; }
    public decimal? ProficiencyBandHigh { get; init; }
    public ConfidenceLabel? Confidence { get; init; }

    // -------------------------------------------------------------------- output C: outcome

    /// <summary>Null until a calibration sample exists. Nothing in the code can fill these by reasoning.</summary>
    public decimal? OutcomeBandLow { get; init; }
    public decimal? OutcomeBandHigh { get; init; }
    public int? CalibrationSampleSize { get; init; }

    // ------------------------------------------------------------------------ the basis

    public required int BasisObservationCount { get; init; }
    public required int ExamLikeObservationCount { get; init; }
    public int? MedianEvidenceAgeDays { get; init; }

    /// <summary>The thresholds this verdict was reached against, so the gate can explain itself.</summary>
    public required ExamThresholdsDto Thresholds { get; init; }

    public required string MethodKey { get; init; }
    public required DateTime ComputedAtUtc { get; init; }
}

/// <summary>One section of the paper and how far the learner has shown anything on it.</summary>
public sealed record ExamAreaCoverageDto
{
    public required string AreaKey { get; init; }
    public required string Label { get; init; }

    /// <summary>This area's share of the whole paper, normalised to sum to one across areas.</summary>
    public required decimal WeightInExam { get; init; }

    /// <summary>0..1 within this area. The figure the per-area floor is applied to.</summary>
    public required decimal Coverage { get; init; }

    public required int TargetCount { get; init; }
    public required int TargetsCovered { get; init; }
    public required int ObservationCount { get; init; }
    public required int ExamLikeObservationCount { get; init; }

    /// <summary>
    /// Weighted accuracy across this area's covered targets, or null when nothing here is
    /// reportable. Never shown without its coverage beside it — an excellent score on a tenth of a
    /// section is not an excellent section.
    /// </summary>
    public decimal? Estimate { get; init; }
}

/// <summary>One actionable thing standing between a learner and a claim about this exam.</summary>
public sealed record ExamGapDto
{
    public required string AreaKey { get; init; }
    public required string AreaLabel { get; init; }
    public Guid? TargetId { get; init; }
    public string? Statement { get; init; }

    public required CoverageGapKind Kind { get; init; }

    /// <summary>What closing it is worth, as a share of the whole paper. The ranking key.</summary>
    public required decimal WeightInExam { get; init; }

    public required int ObservationCount { get; init; }
    public required int ExamLikeObservationCount { get; init; }
    public required int ObservationsNeeded { get; init; }

    /// <summary>A node that teaches this, so the gap can be a button. Null when nothing here does.</summary>
    public Guid? SuggestedNodeId { get; init; }
    public string? SuggestedNodeTitle { get; init; }

    public required int Rank { get; init; }
}

/// <summary>The gates this projection was judged against, carried with the verdict.</summary>
public sealed record ExamThresholdsDto
{
    public required decimal MinCoverageRatio { get; init; }
    public required decimal MinAreaCoverageRatio { get; init; }
    public required int MinObservationsOverall { get; init; }
    public required int MinObservationsPerArea { get; init; }
    public required int MaxMedianEvidenceAgeDays { get; init; }
}

/// <summary>An exam the caller can ask about, for a list screen.</summary>
public sealed record ExamSpecificationSummaryDto
{
    public required Guid ExamSpecificationVersionId { get; init; }
    public required string SpecificationKey { get; init; }
    public required string Name { get; init; }
    public required string VersionLabel { get; init; }
    public required string AuthorityName { get; init; }
    public string? SubjectLabel { get; init; }
    public DateOnly? SittingDate { get; init; }
    public required bool IsPublished { get; init; }

    /// <summary>Targets the blueprint names. A blueprint with none covers nothing by definition.</summary>
    public required int TargetCount { get; init; }

    /// <summary>
    /// How many of those are still lesson placeholders. **Non-zero means this exam cannot be
    /// projected** — a lesson wearing a target's clothes cannot say what a paper examines (§20.5).
    /// </summary>
    public required int PlaceholderTargetCount { get; init; }

    public required int ReportedOutcomes { get; init; }
    public required int CalibrationUsableOutcomes { get; init; }
}
