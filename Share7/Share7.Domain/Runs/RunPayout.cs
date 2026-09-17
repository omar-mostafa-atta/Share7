using Share7.Domain.Economy;

namespace Share7.Domain.Runs;

/// <summary>
/// One line of why a run paid what it paid. Immutable, one row per (source, currency).
/// <para>
/// **Gross and net are both kept, deliberately.** When a child asks why they got 20 after collecting
/// 47, and when somebody is tuning the economy six months from now, "the cap ate 27" has to be
/// answerable from data rather than reconstructed from a valuation table that has changed since.
/// </para>
/// </summary>
public class RunPayout
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }
    public Run? Run { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>
    /// Where the amount came from, as <c>pickup:{kind}</c> or <c>rule:{ruleId}</c>. Two mechanisms
    /// produce a payout and the ledger has to say which — a variable pickup payout and a fixed rule
    /// bonus are tuned in different places and debugged differently.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>How many were reported, before <see cref="PickupValuation.MaxPerRun"/>. Zero for rule bonuses.</summary>
    public int CollectedCount { get; set; }

    /// <summary>How many actually paid, after the per-run cap.</summary>
    public int PaidCount { get; set; }

    /// <summary>The price at settlement time, copied rather than read through — the row can change.</summary>
    public long UnitValue { get; set; }

    /// <summary>What the uncapped claim was worth, before modifiers.</summary>
    public long GrossAmount { get; set; }

    /// <summary>What the caps removed. Positive; <c>Gross - Capped</c> plus modifiers is <see cref="NetAmount"/>.</summary>
    public long CappedAmount { get; set; }

    /// <summary>What was actually granted. This is the number that moved a balance.</summary>
    public long NetAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
