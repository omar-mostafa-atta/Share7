namespace Share7.Domain.Runs;

/// <summary>
/// Where a run is in its lifecycle. **A run must be <see cref="Open"/> before it can settle**, which
/// is the single rule that kills the whole class of "POST a fabricated result" attacks a bare result
/// endpoint would invite: the server knows when the run began, so the reported duration is bounded by
/// real elapsed time and a result for a run nobody started is refused outright.
/// </summary>
public enum RunState
{
    Unknown = 0,

    /// <summary>Started, not yet settled. The only state a result is accepted from.</summary>
    Open,

    /// <summary>Paid. A second result returns the stored settlement rather than paying again.</summary>
    Settled,

    /// <summary>Abandoned past <c>ExpiresAtUtc</c> without a result. Terminal, and pays nothing.</summary>
    Expired,

    /// <summary>
    /// Refused outright rather than capped. Nothing reaches this in phase 1 — capping and flagging
    /// is the response to an implausible run, because a child on a device with a bad clock must not
    /// silently lose a legitimate run. Reserved for seed verification, where a claim can be proven
    /// impossible rather than merely improbable.
    /// </summary>
    Rejected
}

/// <summary>
/// How the run ended, as the client reports it. Recorded for reward rules and analytics; it does not
/// scale the pickup payout, which comes from what was actually collected.
/// </summary>
public enum RunOutcome
{
    Unknown = 0,
    Completed,
    Failed,
    Abandoned
}

/// <summary>
/// A gameplay modifier the run declares was active. **The server owns what each one does** — the
/// client reports that <c>DOUBLE_REWARD</c> was running, never that its own payout should be
/// doubled, because a client that multiplies its own payout controls its own payout.
/// <para>
/// An unrecognised modifier is ignored rather than refused: an older server must not fail a run
/// produced by a newer client, and ignoring it can only ever pay less.
/// </para>
/// </summary>
public enum RunModifierKind
{
    Unknown = 0,

    /// <summary>Doubles what the run's pickups are worth. See <c>RunService</c> for the exact rule.</summary>
    DoubleReward
}
