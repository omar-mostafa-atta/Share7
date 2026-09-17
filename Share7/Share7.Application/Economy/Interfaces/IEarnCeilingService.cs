namespace Share7.Application.Economy.Interfaces;

/// <summary>
/// How much of a currency an account may still **earn from gameplay** today, and the counter that
/// answer is read from.
/// <para>
/// Separate from <see cref="IWalletService"/> deliberately, and the separation is the whole design.
/// A ceiling belongs to *earning*, not to balances: purchased currency must not count against it, or
/// a child whose parent bought coins is blocked from earning any and the purchase becomes actively
/// harmful. Putting this inside the wallet would make every credit subject to it, including the ones
/// that must never be.
/// </para>
/// <para>
/// It is also the bound <c>RewardRule.DailyLimit</c> cannot express. That caps how many times a
/// *rule* fires; with a fixed grant the two coincide, but a run's payout scales with what was
/// collected, so one mispriced valuation row is otherwise unbounded.
/// </para>
/// </summary>
public interface IEarnCeilingService
{
    /// <summary>
    /// What is left of today's allowance for each currency asked about. <see cref="long.MaxValue"/>
    /// for a currency with no ceiling, and never negative — a cap lowered below what somebody has
    /// already earned reads as zero rather than as a debt.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, long>> HeadroomAsync(
        Guid userId,
        IReadOnlyCollection<Guid> currencyIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds to today's earned total, inserting the row on the day's first earning.
    /// <para>
    /// **Call inside the transaction that granted the currency.** A grant without its accrual defeats
    /// the ceiling; an accrual without its grant robs the child. Locked the same way a balance is, so
    /// two runs settling at once serialise rather than both reading the same stale total.
    /// </para>
    /// </summary>
    Task AccrueAsync(
        Guid userId,
        Guid currencyId,
        long amount,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
