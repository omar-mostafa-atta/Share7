

namespace Share7.Domain.Economy;

/// <summary>
/// How much of one currency one user has <b>earned</b> in one UTC day, and over how many runs.
/// <para>
/// **Earned, not held.** Purchased currency is deliberately absent: a ceiling that counted purchases
/// would block a child whose parent bought coins from earning any, which makes the purchase actively
/// harmful. Only gameplay-granted amounts increment this.
/// </para>
/// <para>
/// Phase 1 writes it and enforces nothing — the row has to exist and be accurate before a ceiling can
/// be trusted to read it, and building the counter under the same transaction as the grant is the
/// part that is expensive to retrofit. Phase 2 adds the ceiling on top and needs no schema change.
/// </para>
/// </summary>
public class DailyCurrencyLedger
{
    public Guid UserId { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>Midnight UTC of the day being counted. Date-only; the time component is always zero.</summary>
    public DateTime DayUtc { get; set; }

    /// <summary>Whole units earned from gameplay today. Never decremented — a spend is not an un-earn.</summary>
    public long EarnedAmount { get; set; }

    /// <summary>How many runs contributed, so a farming pattern is visible without joining to Runs.</summary>
    public int RunCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
