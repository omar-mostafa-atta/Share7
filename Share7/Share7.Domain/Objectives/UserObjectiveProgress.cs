namespace Share7.Domain.Objectives;

/// <summary>
/// One player's counter for one objective in one cycle.
/// <para>
/// The cycle is part of the key, so yesterday's daily and today's daily are different rows and a
/// rollover is not an <c>UPDATE</c> — nothing has to run at midnight, and nothing can fail to.
/// </para>
/// </summary>
public class UserObjectiveProgress
{
    public Guid UserId { get; set; }

    public Guid ObjectiveId { get; set; }
    public Objective? Objective { get; set; }

    /// <summary>
    /// Which window this row counts — <c>d:2026-08-23</c>, <c>w:2026-W34</c>, <c>all</c>.
    /// <para>
    /// **Derived, then stored.** Derived so no scheduler is needed and a rollover cannot be missed;
    /// stored so that changing the reset hour later cannot retroactively move a child's finished
    /// quest into a different day. See <c>ObjectiveCycle</c>.
    /// </para>
    /// </summary>
    public string CycleKey { get; set; } = string.Empty;

    /// <summary>The counter, in the metric's unit. Never negative.</summary>
    public long Value { get; set; }

    public ObjectiveState State { get; set; } = ObjectiveState.InProgress;

    /// <summary>When the target was reached. Null while still counting.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// When the reward was taken. Null until claimed, and — with the reward engine's own
    /// idempotency index behind it — what makes double payment structurally impossible.
    /// </summary>
    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>
    /// When this row stops being claimable. Null for an objective that never expires.
    /// <para>
    /// **A finished daily does not die at midnight.** A child who completed their quests and then
    /// did not open the app before the cycle rolled has earned that reward; expiring it punishes
    /// them for going to bed. Only <see cref="ObjectiveState.InProgress"/> rows expire at the
    /// cycle boundary — a completed one stays claimable for a generous retention beyond it.
    /// </para>
    /// </summary>
    public DateTime? ClaimableUntilUtc { get; set; }

    /// <summary>
    /// The highest <c>GameResult.Sequence</c> folded into <see cref="Value"/>.
    /// <para>
    /// Per-row rather than only global, because the projector runs two ways: inline for the player
    /// who just acted, and in batch for backfill and repair. Both must be able to tell a result
    /// they have already counted from one they have not — and for a <c>Sum</c> counter that is not
    /// derivable from the total, which is the same trap <c>GameResult.ProjectedAtUtc</c> documents
    /// for leaderboard entries.
    /// </para>
    /// </summary>
    public long LastSequence { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
