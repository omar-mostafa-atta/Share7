using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;

namespace Share7.Application.Commerce.Interfaces;

/// <summary>
/// Who owns what. The only way an entitlement is created — purchase, admin grant and anything
/// later all come through here, so the ownership invariants live in one place.
/// </summary>
public interface IEntitlementService
{
    /// <summary>
    /// Everything this account owns, newest first. Includes products that have been retired or
    /// delisted: ownership outlives the shop.
    /// </summary>
    Task<IReadOnlyList<EntitlementDto>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a product to an account.
    /// <para>
    /// **Idempotent.** Granting something already owned returns the existing entitlement with
    /// <c>AlreadyOwned</c> set rather than failing or writing a second row — the unique
    /// (user, product) index is what enforces that, so it holds under concurrency too. A caller
    /// that must *refuse* a repeat, such as a purchase that would otherwise charge for something
    /// already owned, checks before granting.
    /// </para>
    /// <para>
    /// **Joins an ambient transaction** when one is open rather than opening its own, so a purchase
    /// can deduct currency and grant entitlements as a single unit.
    /// </para>
    /// </summary>
    Task<ServiceResult<EntitlementGrantResult>> GrantAsync(
        Guid userId,
        Guid productId,
        EntitlementSource source,
        string? sourceId = null,
        CancellationToken cancellationToken = default);
}
