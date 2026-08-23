namespace Share7.Domain.Objectives;

/// <summary>
/// What kind of objective this is — which is, almost entirely, a statement about its cycle.
/// <para>
/// **These are not separate systems.** A daily quest, a weekly quest and an achievement differ in
/// how often their counter resets and in nothing else; they share one definition table, one counter
/// table, one projector and one payout path. Modelling them apart is how a platform ends up with
/// several counters that drift and several ways to double-pay a child.
/// </para>
/// </summary>
public enum ObjectiveKind
{
    /// <summary>Resets every day.</summary>
    Daily = 0,

    /// <summary>Resets every ISO week.</summary>
    Weekly = 1,

    /// <summary>Resets every calendar month.</summary>
    Monthly = 2,

    /// <summary>
    /// Bound to an authored season rather than a recurring window. The season's key is the cycle,
    /// so a season is ended by retiring the objective, never by a rollover.
    /// </summary>
    Seasonal = 3,

    /// <summary>
    /// Never resets. One counter, forever — which is all an achievement is.
    /// <para>
    /// Its badge, if it has one, is a separate concern entirely: an entitlement granted by the
    /// reward rule that pays for completion, not a column here.
    /// </para>
    /// </summary>
    Achievement = 4
}

/// <summary>
/// Where one player stands on one objective in one cycle.
/// <para>
/// **Completion and payment are deliberately separate states.** The claim is the reward moment and
/// deserves to be a deliberate act; more importantly, a payout that fails must never be able to
/// erase the fact that the objective was finished. <c>Completed</c> is the child's, and no failure
/// downstream of it can take it away.
/// </para>
/// </summary>
public enum ObjectiveState
{
    /// <summary>Counting. The only state a cycle rollover is allowed to expire.</summary>
    InProgress = 0,

    /// <summary>Target reached, reward not yet taken.</summary>
    Completed = 1,

    /// <summary>Reward paid. Terminal.</summary>
    Claimed = 2,

    /// <summary>
    /// The cycle ended before the target was reached. Terminal, and never applied to a
    /// <see cref="Completed"/> row — see <c>UserObjectiveProgress</c>.
    /// </summary>
    Expired = 3
}
