namespace Share7.Domain.Economy;

/// <summary>
/// What one gameplay signal is worth. **The entire economy-tuning surface**, and the reason
/// rebalancing is an <c>UPDATE</c> rather than a client release: coin value used to live in 200
/// prefabs across 200 mini-games, so a nerf meant shipping a build and waiting for stores to approve
/// it.
/// <para>
/// This exists because reward rules cannot express it. A <c>RewardRule</c> grants a *fixed* amount —
/// "10 coins when a lesson is completed" — and there is no shape in it for "1 coin per coin
/// collected" or "5 XP per correct answer", because the payout varies with the session. Forcing that
/// into rules would mean one rule per possible count, which is not a workaround but a different,
/// worse table.
/// </para>
/// <para>
/// Fixed bonuses — completed a run, first of the day, a perfect lesson — are still reward rules, on
/// the <c>RUN_SETTLED</c> and lesson events. Two mechanisms, one granting path: both end at
/// <c>IWalletService</c>, because a second way to move a balance is how an economy grows two
/// disagreeing coin counters.
/// </para>
/// <para>
/// **Renamed from <c>PickupValuation</c> (2026-08-25), and generalised to two producers.** It now
/// prices anything counted — a coin picked up, an obstacle dodged, a question answered right — for
/// both a settled run and a graded attempt. <see cref="SignalKinds.OwnerOf"/> decides which surface
/// may report each kind, so one table serves both without either being able to pay for the other's
/// work.
/// </para>
/// </summary>
public class SignalValuation
{
    public Guid Id { get; set; }

    /// <summary>
    /// Which game this price applies to, or **null for the platform default**. Resolution is exact
    /// match first, then the default, then "this kind pays nothing" — which is a design oversight to
    /// notice in the payout data, not a reason to fail a child's session.
    /// </summary>
    public Guid? GameId { get; set; }

    /// <summary>A <see cref="SignalKinds"/> token. Stored lowercase; lookups normalise before matching.</summary>
    public string SignalKind { get; set; } = string.Empty;

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>Whole units one signal is worth. Zero is legal and means "counted, but not currency".</summary>
    public long UnitValue { get; set; }

    /// <summary>
    /// Most of this kind a single session can be paid for. **Not optional** — an unbounded row is an
    /// unbounded payout, and the account's daily ceiling is opt-in per currency, so for an uncapped
    /// currency this is the only bound there is.
    /// </summary>
    public int MaxPerRun { get; set; }

    /// <summary>
    /// Most of this kind one user can be paid for in a UTC day, across every session and both
    /// surfaces. Null means no per-kind daily bound.
    /// <para>
    /// Counted in <c>DailySignalLedger</c> rather than by scanning payout history, so the check is a
    /// single keyed read whose cost does not grow with how long the platform has been live.
    /// </para>
    /// </summary>
    public int? MaxPerDay { get; set; }

    /// <summary>
    /// The per-second plausibility bound for **this kind**, or null to use the platform default in
    /// <c>RunOptions.MaxPickupsPerSecond</c>.
    /// <para>
    /// **Added with distance.** One global bound was right while <c>coin</c> was the only kind: a
    /// child collects perhaps two a second, so twenty was generous. A runner covers ten metres a
    /// second, and the same bound would silently cap every honest run's distance to half of what it
    /// was. A bound that is wrong for a kind is worse than no bound, because it fires on legitimate
    /// play and looks like the game cheating.
    /// </para>
    /// <para>
    /// Ignored on the attempt surface, which has no duration to divide by: a graded attempt is
    /// bounded by the number of questions in the lesson, which is a far tighter bound than any rate.
    /// </para>
    /// </summary>
    public double? MaxPerSecond { get; set; }

    /// <summary>
    /// Switches a price off without deleting it. Retiring rather than deleting keeps historical
    /// payout rows explicable — a payout has to stay answerable to "why that much?".
    /// </summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
