namespace Share7.Application.Measurement.Interfaces;

/// <summary>
/// Turns responses into observations, and folds item statistics as it goes.
/// <para>
/// **The first place meaning appears.** A response is a fact about what a child did; an observation
/// is what it says about a claim. Everything this writes is derived — delete it all and running the
/// projector again rebuilds it from the immutable log, which is the property the recompute line
/// rests on and the reason re-targeting items is affordable.
/// </para>
/// </summary>
public interface IObservationProjector
{
    /// <summary>
    /// Projects one learner's unobserved responses. Used on the learner's own read path, so what
    /// they are shown accounts for what they just did.
    /// </summary>
    Task<ObservationProjectionReport> ProjectForLearnerAsync(
        Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Projects everything pending across all learners, bounded by
    /// <c>ProjectionConsumers.Observations</c> and by <paramref name="maxResponses"/>.
    /// </summary>
    Task<ObservationProjectionReport> ProjectPendingAsync(
        int maxResponses = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws away and rebuilds every observation belonging to the named items, against whatever
    /// the target mappings say now.
    /// <para>
    /// **What re-targeting costs, and the reason it is affordable.** The ordinary projector skips
    /// a response that already has an observation, so a remap would otherwise leave the old claim
    /// standing forever. This deletes the derived rows and regenerates them from the immutable
    /// responses — the recompute line doing exactly what it was built for.
    /// </para>
    /// <para>
    /// **Exclusion annotations survive.** They are the one thing on an observation that was never
    /// derived — a human said this answer does not count — and rebuilding over them would silently
    /// readmit a mis-keyed item's fifty thousand answers. They are captured before the delete and
    /// re-applied to the rows that replace them.
    /// </para>
    /// <para>
    /// Item statistics are deliberately <b>not</b> refolded: they are a property of the item, not
    /// of the target it is mapped to, so a remap does not change a single one of them.
    /// </para>
    /// </summary>
    Task<ReprojectionReport> ReprojectItemsAsync(
        IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);
}

/// <summary>What a targeted rebuild replaced.</summary>
public sealed record ReprojectionReport
{
    public int ItemsAffected { get; init; }
    public int ResponsesRead { get; init; }
    public int ObservationsDeleted { get; init; }
    public int ObservationsWritten { get; init; }

    /// <summary>Human exclusion decisions carried across the rebuild rather than lost to it.</summary>
    public int ExclusionsPreserved { get; init; }

    /// <summary>Learners whose measurements are now stale and will be recomputed on next read.</summary>
    public int LearnersAffected { get; init; }
}

/// <summary>What one projection pass did.</summary>
public sealed record ObservationProjectionReport
{
    public int ResponsesRead { get; init; }
    public int ObservationsWritten { get; init; }

    /// <summary>
    /// Responses that produced nothing because their item maps to no learning target. Worth
    /// counting rather than swallowing: it is the measurable definition of content that cannot be
    /// measured, and it is the first number the admin quality surface should show.
    /// </summary>
    public int UnmappedResponses { get; init; }

    public int ItemStatisticsUpdated { get; init; }
    public long Watermark { get; init; }
}
