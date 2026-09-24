using Share7.Domain.Competency;

namespace Share7.Domain.Assessment;

/// <summary>
/// What Share7 is willing to say about how a learner would do in one examination.
/// <para>
/// <b>The sufficiency verdict is the feature, not the guard on the feature.</b> When the gates
/// fail this entity does not return a smaller, hedged number — it returns the reason and the gaps,
/// and the gaps are more useful two months before a paper than any score would have been: "you
/// have exam-quality evidence on 62% of what this exam covers, and the missing part is geometry
/// and statistics" is actionable, true, and something no competitor says honestly (§6.2, output A).
/// </para>
/// <para>
/// Derived and disposable like everything else below the recompute line, and **one live row per
/// learner, exam version and method** rather than a history: the learner's own read recomputes it
/// anyway, so the row exists to make somebody else's read cheap. A teacher opening a class of
/// thirty wants thirty stored figures, not thirty recomputations — and a stale one is visibly
/// stale, because <see cref="ComputedAtUtc"/> travels with it.
/// </para>
/// <para>
/// <b>There is no field here capable of carrying a point prediction.</b> That is deliberate and it
/// is structural: §6.4 asks for bands only, and the cheapest way to guarantee a client cannot
/// render "you will get 61%" is to give the server nowhere to put it.
/// </para>
/// </summary>
public class ExamProjection
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid ExamSpecificationVersionId { get; set; }
    public ExamSpecificationVersion? ExamSpecificationVersion { get; set; }

    public ProjectionSufficiency Sufficiency { get; set; }

    // ---------------------------------------------------------------- output A: coverage

    /// <summary>
    /// The share of blueprint weight the learner has admissible evidence on, 0..1.
    /// **Always present, including when everything else is null** — coverage is arithmetic over the
    /// blueprint and the evidence, it needs no statistics and no calibration, and it is the part of
    /// this entity that works on day one.
    /// </summary>
    public decimal CoverageRatio { get; set; }

    /// <summary>The weakest area's coverage. The number that catches a blind spot an average hides.</summary>
    public decimal WeakestAreaCoverage { get; set; }

    /// <summary>Admitted observations behind the whole figure. The honest denominator.</summary>
    public int BasisObservationCount { get; set; }

    /// <summary>How many of those were collected under conditions an exam claim can rest on.</summary>
    public int ExamLikeObservationCount { get; set; }

    /// <summary>Median age of the admitted evidence, in days. Null when there is none.</summary>
    public int? MedianEvidenceAgeDays { get; set; }

    // --------------------------------------------------- output B: criterion proficiency

    /// <summary>
    /// Blueprint-weighted proficiency across the covered targets, 0..1. **Not a predicted score**
    /// — it is what the learner's own accuracy has been on what the exam asks about, weighted the
    /// way the exam weights it. Null unless <see cref="Sufficiency"/> is
    /// <see cref="ProjectionSufficiency.Sufficient"/> or <see cref="ProjectionSufficiency.Uncalibrated"/>,
    /// which are the two states in which the learner-side claims actually hold.
    /// </summary>
    public decimal? ProficiencyBandLow { get; set; }
    public decimal? ProficiencyBandHigh { get; set; }

    /// <summary>A word, never a number. §6.4.</summary>
    public ConfidenceLabel? Confidence { get; set; }

    // --------------------------------------------------------- output C: projected outcome

    /// <summary>
    /// The empirical band, in the exam's own marks, from the calibration sample.
    /// <b>Null until a calibration exists</b>, which is the state every exam is in and will remain
    /// in until §6.5 has collected a few hundred matched pairs. Nothing computes these from
    /// reasoning, and the absence of a code path that could is the point.
    /// </summary>
    public decimal? OutcomeBandLow { get; set; }
    public decimal? OutcomeBandHigh { get; set; }

    /// <summary>How many reported outcomes the band was fitted on. Null when there is no band.</summary>
    public int? CalibrationSampleSize { get; set; }

    // ------------------------------------------------------------------------ provenance

    /// <summary>The method that produced this, so an old projection stays interpretable (§5.3).</summary>
    public string MethodKey { get; set; } = ProjectionMethods.CoverageV1;

    public DateTime ComputedAtUtc { get; set; }

    public ICollection<ExamProjectionGap> Gaps { get; set; } = new List<ExamProjectionGap>();
}

/// <summary>
/// One thing standing between a learner and a defensible claim about this exam.
/// <para>
/// <b>This is the actionable payload</b> and the reason an insufficient projection is a normal,
/// useful response rather than an error. Ranked by what it is worth in the paper, so the list reads
/// as "do this next" rather than as a catalogue of failures.
/// </para>
/// </summary>
public class ExamProjectionGap
{
    public Guid Id { get; set; }

    public Guid ExamProjectionId { get; set; }
    public ExamProjection? ExamProjection { get; set; }

    /// <summary>The blueprint area this gap is in. Present even for a target-level gap.</summary>
    public string AreaKey { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;

    /// <summary>
    /// The specific target, when the gap is one claim rather than a whole area. Null for an
    /// area-level gap, which is what a completely untouched section produces.
    /// </summary>
    public Guid? TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    public CoverageGapKind Kind { get; set; }

    /// <summary>
    /// What this gap is worth in the paper, 0..1 of total blueprint weight. **The ranking key** —
    /// a blind spot worth a fifth of the marks is not the same advice as one worth two percent.
    /// </summary>
    public decimal WeightInExam { get; set; }

    /// <summary>Admitted observations the learner has here. Often zero, which is the clearest message.</summary>
    public int ObservationCount { get; set; }

    /// <summary>How many of those were exam-like. The difference between the two is its own diagnosis.</summary>
    public int ExamLikeObservationCount { get; set; }

    /// <summary>How many more exam-like observations would close it. Concrete, so it can be acted on.</summary>
    public int ObservationsNeeded { get; set; }

    /// <summary>
    /// A curriculum node that teaches this target, so the gap can be a button rather than a
    /// sentence. Null when nothing in the enrolled structure covers it — which is itself worth
    /// knowing, because it means the learner cannot close this gap here at all.
    /// </summary>
    public Guid? SuggestedNodeId { get; set; }

    public int Rank { get; set; }
}

/// <summary>
/// The methods that can produce a projection. A string for the same reason measurement methods are:
/// they coexist rather than replace one another, and an old row has to stay readable.
/// </summary>
public static class ProjectionMethods
{
    /// <summary>
    /// Blueprint coverage arithmetic plus classical proficiency over covered targets. No
    /// calibration, no item response theory, and therefore no output C — which is exactly what the
    /// data currently supports and exactly as far as it goes.
    /// </summary>
    public const string CoverageV1 = "coverage_v1";
}
