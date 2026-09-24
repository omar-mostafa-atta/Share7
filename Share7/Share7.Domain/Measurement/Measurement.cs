using Share7.Domain.Competency;

namespace Share7.Domain.Measurement;

/// <summary>
/// learner × target × method. An estimate, an interval and an N — **never a bare number**.
/// <para>
/// Fully derived and disposable: delete every row and the measurement service rebuilds it from the
/// observations, which rebuild from the responses. That is what makes re-targeting affordable — when
/// a real framework replaces the lesson placeholders, the mappings change and every historical
/// measurement follows, without a single response being touched.
/// </para>
/// <para>
/// **The interval is not decoration.** Four out of five is not 80% proficiency; it is a wide band
/// centred near 80%, and the difference is the whole distance between a defensible claim and a
/// made-up one. No DTO in this system exposes <see cref="Estimate"/> without
/// <see cref="IntervalLow"/>, <see cref="IntervalHigh"/> and <see cref="ObservationCount"/>.
/// </para>
/// </summary>
public class Measurement
{
    public Guid Id { get; set; }

    public Guid LearnerId { get; set; }

    public Guid TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    /// <summary>
    /// Which method produced this. Methods coexist — a second one writes its own row rather than
    /// overwriting, so a new approach can be shadowed against the live one before anybody trusts it.
    /// </summary>
    public string MethodKey { get; set; } = MeasurementMethods.ClassicalV1;

    /// <summary>
    /// Proportion correct under the method's admission rule. 0 to 1.
    /// <para>
    /// This is **not** a mastery score and must never be rendered as a percentage on its own. It is
    /// the centre of an interval whose width is the actual information content.
    /// </para>
    /// </summary>
    public decimal Estimate { get; set; }

    /// <summary>Wilson score interval, lower bound. Honest at small N, which is where all the data is.</summary>
    public decimal IntervalLow { get; set; }

    /// <summary>Wilson score interval, upper bound.</summary>
    public decimal IntervalHigh { get; set; }

    /// <summary>How many observations went in. The number that decides whether any of this is sayable.</summary>
    public int ObservationCount { get; set; }

    /// <summary>
    /// How many of those were <c>Assessment</c> class. The only count an exam-facing claim may read,
    /// which is why it is stored separately rather than recomputed at report time.
    /// </summary>
    public int AssessmentCount { get; set; }

    public int CorrectCount { get; set; }

    /// <summary>The highest observation sequence this estimate accounts for.</summary>
    public long LastObservationSequence { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
