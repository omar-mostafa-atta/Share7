using Share7.Domain.Competency;
using Share7.Domain.Content;
using Share7.Domain.Evidence;

namespace Share7.Domain.Measurement;

/// <summary>
/// One response, interpreted against **one** learning target.
/// <para>
/// This is the first derived layer and the first place meaning appears. A
/// <see cref="LearnerResponse"/> says what a child did; an observation says what it tells us about
/// a claim. An item measuring two targets produces two observations from one response, which is
/// why this is a separate table rather than a column.
/// </para>
/// <para>
/// **Append-only and annotatable, never updated in place.** It is fully derived — delete every row
/// and the projector rebuilds it from the immutable response log, which is the property the whole
/// recompute line rests on. The one thing that is not rebuildable is
/// <see cref="ExcludedAtUtc"/>: a human decision about validity, which survives a rebuild because
/// it was never derived in the first place.
/// </para>
/// </summary>
public class Observation
{
    public Guid Id { get; set; }

    /// <summary>Monotonic. Measurement tracks a watermark over it, as this table does over responses.</summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The response this interprets. Null when the source is a contracted non-item interaction —
    /// which nothing produces yet, and the column exists so that the day one does, the measurement
    /// layer does not need reshaping.
    /// </summary>
    public Guid? LearnerResponseId { get; set; }
    public LearnerResponse? LearnerResponse { get; set; }

    /// <summary>Denormalised from the response, because every measurement read filters by it.</summary>
    public Guid LearnerId { get; set; }

    public Guid TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    public Guid ItemVersionId { get; set; }

    public ObservationOutcome Outcome { get; set; }

    /// <summary>
    /// The strength class this observation carries, **derived at projection time** from the
    /// contract, the conditions and the item bank's trust ceiling — never stored on the response
    /// and never asserted by a caller. An unreviewed teacher-authored item produces
    /// <see cref="EvidenceStrength.Practice"/> here however controlled the sitting was, which is
    /// the entire teacher-authoring safety mechanism (§10.2).
    /// </summary>
    public EvidenceStrength Strength { get; set; }

    /// <summary>
    /// Item emphasis × contract weight. How much this observation counts, separately from how far
    /// it can be trusted.
    /// </summary>
    public decimal Weight { get; set; } = 1.0m;

    public Guid EvidenceContractVersionId { get; set; }

    /// <summary>Set when the answer was produced collaboratively. Discounted, not credited.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Where it was collected. Provenance for reporting; never part of what it means.</summary>
    public Guid? NodeId { get; set; }

    public DateTime ObservedAtUtc { get; set; }

    /// <summary>
    /// When this observation stopped counting. **The row stays.** See
    /// <see cref="ObservationExclusionReason"/> for why deleting would be the wrong move.
    /// </summary>
    public DateTime? ExcludedAtUtc { get; set; }

    public ObservationExclusionReason? ExclusionReason { get; set; }

    /// <summary>Who excluded it, when a person did. Null for automatic exclusions.</summary>
    public Guid? ExcludedByUserId { get; set; }

    public string? ExclusionNote { get; set; }
}
