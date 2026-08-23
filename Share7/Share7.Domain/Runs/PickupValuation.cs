using Share7.Domain.Economy;

namespace Share7.Domain.Runs;

/// <summary>
/// What one pickup is worth. **The entire economy-tuning surface**, and the reason rebalancing is an
/// <c>UPDATE</c> rather than a client release: coin value used to live in 200 prefabs across 200
/// mini-games, so a nerf meant shipping a build and waiting for stores to approve it.
/// <para>
/// This exists because reward rules cannot express it. A <c>RewardRule</c> grants a *fixed* amount —
/// "10 coins when a lesson is completed" — and there is no shape in it for "1 coin per coin
/// collected", because the payout varies with the run. Forcing that into rules would mean one rule
/// per possible count, which is not a workaround but a different, worse table.
/// </para>
/// <para>
/// Fixed run bonuses — completed a run, first of the day, a perfect run — are still reward rules, on
/// the <c>RUN_SETTLED</c> event. Two mechanisms, one granting path: both end at
/// <c>IWalletService</c>, because a second way to move a balance is how an economy grows two
/// disagreeing coin counters.
/// </para>
/// </summary>
public class PickupValuation
{
    public Guid Id { get; set; }

    /// <summary>
    /// Which game this price applies to, or **null for the platform default**. Resolution is exact
    /// match first, then the default, then "this kind pays nothing" — which is a design oversight to
    /// notice in the payout data, not a reason to fail a child's run.
    /// </summary>
    public Guid? GameId { get; set; }

    /// <summary>A <see cref="PickupKinds"/> token. Stored lowercase; lookups normalise before matching.</summary>
    public string PickupKind { get; set; } = string.Empty;

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>Whole units one pickup is worth. Zero is legal and means "collectible, but not currency".</summary>
    public long UnitValue { get; set; }

    /// <summary>
    /// Most of this kind a single run can be paid for. **Not optional** — an unbounded row is an
    /// unbounded payout, and the daily ceiling that would otherwise catch it does not land until
    /// phase 2.
    /// </summary>
    public int MaxPerRun { get; set; }

    /// <summary>
    /// Most of this kind one user can be paid for in a UTC day, across every run. Null means no
    /// per-kind daily bound; the account-wide ceiling in <c>DailyCurrencyLedger</c> is what phase 2
    /// enforces on top. Recorded now so the column does not need adding under load later.
    /// </summary>
    public int? MaxPerDay { get; set; }

    /// <summary>
    /// Switches a price off without deleting it. Retiring rather than deleting keeps historical
    /// <see cref="RunPayout"/> rows explicable — a payout has to stay answerable to "why that much?".
    /// </summary>
    public bool Enabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
