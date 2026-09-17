using Share7.Domain.Economy;

namespace Share7.Domain.Rewards;

/// <summary>
/// One currency and quantity a <see cref="RewardRule"/> pays. A rule with three of these pays all
/// three or none — there is no partial payout.
/// <para>
/// This child table is what makes a reward multi-currency. Adding gems to an existing coin reward
/// is an <c>INSERT</c>, not a migration.
/// </para>
/// </summary>
public class RewardRuleGrant
{
    public Guid Id { get; set; }

    public Guid RewardRuleId { get; set; }
    public RewardRule? RewardRule { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>
    /// Whole units, always positive. A rule takes nothing away — deductions are purchases or
    /// admin corrections, and both have their own paths onto the ledger.
    /// </summary>
    public long Amount { get; set; }
}
