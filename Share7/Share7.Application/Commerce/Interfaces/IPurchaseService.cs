using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;

namespace Share7.Application.Commerce.Interfaces;

/// <summary>
/// Buying an offer: the one operation in this system that takes something away from an account.
/// <para>
/// Three guarantees, in the order they matter:
/// </para>
/// <para>
/// **Atomic.** The currency debit, every entitlement, the ledger entries and the transaction row all
/// commit together or none of them do. There is no state where an account has been charged and not
/// granted.
/// </para>
/// <para>
/// **Idempotent.** <c>requestId</c> is the client's key. Replaying it returns the original outcome —
/// including the original refusal — rather than evaluating again, so a retry after a timeout cannot
/// charge twice. The unique <c>(UserId, RequestId)</c> index is what enforces it, so it holds when
/// two requests land at the same instant.
/// </para>
/// <para>
/// **Answerable.** A refusal is written down too, with its reason. "It took my coins and gave me
/// nothing" is a question the transaction table can answer.
/// </para>
/// </summary>
public interface IPurchaseService
{
    /// <summary>
    /// A business refusal — too few coins, expired, limit reached — comes back as a
    /// <see cref="ServiceResult"/> failure that still carries a full
    /// <see cref="PurchaseResponse"/>, so the caller gets authoritative balances from the same round
    /// trip. Only an exception means the outcome is unknown.
    /// </summary>
    Task<ServiceResult<PurchaseResponse>> PurchaseAsync(
        Guid userId,
        PurchaseRequest request,
        CancellationToken cancellationToken = default);
}
