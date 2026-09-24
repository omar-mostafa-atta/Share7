using Share7.Domain.Competency;

namespace Share7.Domain.Measurement;

/// <summary>
/// A published, versioned statement of what "mastered" means here.
/// <para>
/// **A verdict without a readable rule is an opinion with a badge on it.** Every
/// <see cref="MasteryVerdict"/> names the exact rule version that produced it, so a parent asking
/// "why does it say my daughter has not mastered this" has an answer that does not depend on
/// somebody remembering what the thresholds were in March.
/// </para>
/// </summary>
public class MasteryRule
{
    public Guid Id { get; set; }

    public string RuleKey { get; set; } = string.Empty;
    public int VersionNumber { get; set; }

    /// <summary>
    /// The sufficiency gate. Below this many admitted observations the verdict is
    /// <see cref="MasteryState.Insufficient"/> and no estimate is reported at all.
    /// **The gate is the feature** — it is what stops the system inventing a claim from four
    /// answers.
    /// </summary>
    public int MinObservations { get; set; }

    /// <summary>
    /// How strong the evidence has to be to count toward this rule. Practice-class evidence can
    /// support "developing"; it cannot support a claim that generalises to an examination.
    /// </summary>
    public Evidence.EvidenceStrength MinStrength { get; set; } = Evidence.EvidenceStrength.Practice;

    /// <summary>
    /// Mastery is claimed only when the interval's **lower bound** clears this. Using the point
    /// estimate would let four-out-of-four read as mastery, which is the single most common way an
    /// education product lies to a parent.
    /// </summary>
    public decimal MasteredIntervalLowAtLeast { get; set; }

    /// <summary>Below mastery, the point estimate at which a learner is called "developing".</summary>
    public decimal DevelopingEstimateAtLeast { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Applies this rule to a measurement. A pure function of the rule and the numbers, so a
    /// verdict can always be re-derived and checked against what was stored.
    /// </summary>
    public MasteryState Decide(int admittedObservations, decimal estimate, decimal intervalLow)
    {
        if (admittedObservations < MinObservations)
            return MasteryState.Insufficient;

        if (intervalLow >= MasteredIntervalLowAtLeast)
            return MasteryState.Mastered;

        return estimate >= DevelopingEstimateAtLeast
            ? MasteryState.Developing
            : MasteryState.NotMet;
    }
}

/// <summary>
/// One judgement about one learner and one target, naming the rule that produced it.
/// <para>
/// Derived and disposable like <see cref="Measurement"/>. Kept as a row rather than computed on
/// read so that reporting is cheap and so that a verdict's history is visible: a child moving from
/// <see cref="MasteryState.Insufficient"/> to <see cref="MasteryState.Developing"/> is a real event
/// and it is worth being able to say when it happened.
/// </para>
/// </summary>
public class MasteryVerdict
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    public Guid MasteryRuleId { get; set; }
    public MasteryRule? MasteryRule { get; set; }

    public Guid MeasurementId { get; set; }
    public Measurement? Measurement { get; set; }

    public MasteryState State { get; set; }

    /// <summary>
    /// How many observations the rule actually admitted — which is not the measurement's total when
    /// the rule demands a strength floor. Stored so the verdict explains itself without a join.
    /// </summary>
    public int AdmittedObservations { get; set; }

    public DateTime DecidedAtUtc { get; set; }
}
