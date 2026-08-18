using Share7.Application.Common.Models;
using Share7.Application.Economy.Models;

namespace Share7.Application.Economy.Interfaces;

/// <summary>
/// The only way a balance is allowed to change. Every mutation writes the balance row and an
/// immutable ledger entry in one transaction — nothing else in the codebase should touch
/// <c>UserCurrencyBalances</c> directly.
/// </summary>
public interface IWalletService
{
    /// <summary>Absolute balances for a user, one per currency they hold.</summary>
    Task<IReadOnlyList<BalanceDto>> GetBalancesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a signed delta atomically.
    /// <para>
    /// Takes a row lock on the (user, currency) balance for the duration, so concurrent
    /// mutations serialise rather than racing, and <c>BalanceAfter</c> on the ledger entry is
    /// exact. Debits that would take the balance below zero are refused with
    /// <c>INSUFFICIENT_BALANCE</c> and change nothing.
    /// </para>
    /// <para>
    /// **Joins an ambient transaction when one is open** rather than opening its own, so a
    /// purchase can charge and grant as a single unit.
    /// </para>
    /// </summary>
    Task<ServiceResult<WalletMutationResult>> ApplyAsync(
        WalletMutation mutation,
        CancellationToken cancellationToken = default);
}
