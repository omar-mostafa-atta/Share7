namespace Share7.Domain.Assessment;

/// <summary>
/// What a learner actually got, in a real examination Share7 did not administer.
/// <para>
/// <b>The only thing on this table that cannot be engineered.</b> Claims 1–3 of §6.1 — construct
/// alignment, representativeness, comparable conditions — are all buildable by reasoning about the
/// model. Claim 4, the mapping from a proficiency estimate to a score, is not: it needs observed
/// pairs of <i>(what we said, what happened)</i>, and no amount of design produces one. Every row
/// here is one pair, and until a few hundred of them exist for a specification version, output C
/// does not render at all.
/// </para>
/// <para>
/// Collected from day one because the data is irreplaceable and the cost is a form. A platform
/// that waits until it "needs" calibration data waits another two years after that for the data to
/// accrue (§6.5, ADR-E16).
/// </para>
/// </summary>
public class ReportedExamOutcome
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid ExamSpecificationVersionId { get; set; }
    public ExamSpecificationVersion? ExamSpecificationVersion { get; set; }

    /// <summary>
    /// The mark, on the specification's own scale. Nullable because plenty of systems report only
    /// a grade, and a grade with no score is still a usable calibration point.
    /// </summary>
    public decimal? ReportedScore { get; set; }

    /// <summary>The grade as the body awards it — "A", "ممتاز", "7". Never parsed into a number.</summary>
    public string? ReportedGrade { get; set; }

    /// <summary>
    /// Denormalised from the specification version at capture time. A body that restates a paper's
    /// maximum afterwards must not silently rescale results already reported against the old one.
    /// </summary>
    public int? MaxScore { get; set; }

    public DateOnly SittingDate { get; set; }

    public OutcomeVerification Verification { get; set; } = OutcomeVerification.SelfReported;

    /// <summary>Who supplied it — the learner, a guardian, a school. Null when self-reported.</summary>
    public Guid? ReportedByUserId { get; set; }

    /// <summary>The organization that supplied it, for school-supplied results.</summary>
    public Guid? OrgId { get; set; }

    // -------------------------------------------------------------------------- consent

    /// <summary>
    /// <b>Explicit, and guardian-gated for a minor.</b> An exam result is among the most sensitive
    /// facts a platform can hold about a child, and it is being collected for a purpose — improving
    /// a model — that is not the purpose the child came here for. Recording the consent decision
    /// beside the datum, rather than in a separate system nobody joins to, is what makes honouring
    /// a withdrawal a single query.
    /// </summary>
    public bool ConsentedToCalibrationUse { get; set; }

    /// <summary>Who granted it. The guardian for a minor, the learner otherwise.</summary>
    public Guid? ConsentGrantedByUserId { get; set; }

    public DateTime? ConsentGrantedAtUtc { get; set; }

    /// <summary>
    /// Set when consent is withdrawn. **The row stays and stops being used**, exactly as an
    /// excluded observation does: deleting would destroy the evidence that the result was once
    /// counted and would leave a calibration fitted on a sample nobody can reconstruct.
    /// </summary>
    public DateTime? ConsentWithdrawnAtUtc { get; set; }

    /// <summary>
    /// The learner's own snapshot at the moment they reported, so a later recompute of the
    /// measurement layer cannot quietly move the calibration sample underneath a fitted model.
    /// Null when nothing was measurable at the time, which is itself a usable fact.
    /// </summary>
    public decimal? BlueprintWeightedEstimateAtReport { get; set; }
    public decimal? CoverageRatioAtReport { get; set; }
    public int ObservationCountAtReport { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Free text from whoever reported it. Read by a human, never parsed.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Whether this row may be used to fit a calibration. Consent held, not disputed, and a usable
    /// score — all three, because a sample assembled from rows failing any of them is worse than
    /// no sample.
    /// </summary>
    public bool IsCalibrationUsable =>
        ConsentedToCalibrationUse
        && ConsentWithdrawnAtUtc is null
        && Verification != OutcomeVerification.Disputed
        && (ReportedScore is not null || !string.IsNullOrWhiteSpace(ReportedGrade));
}
