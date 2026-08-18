using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;

namespace Share7.Application.Commerce.Interfaces;

/// <summary>
/// Authoring for what a product hands over. Admin-only: these rows are what a purchase reads to
/// decide what an account receives, so a player able to write one could grant themselves anything.
/// <para>
/// **The whole set freezes once any account owns the product.** An entitlement resolves what it owns
/// by reading these rows on every read rather than snapshotting them at purchase time, so adding,
/// editing or deleting one would retroactively change what existing owners have. Every write here
/// is refused with <c>PRODUCT_GRANTS_LOCKED</c> in that case — author a replacement product instead.
/// </para>
/// </summary>
public interface IProductGrantAdminService
{
    /// <summary>Every grant, or just one product's when <paramref name="productId"/> is given.</summary>
    Task<IReadOnlyList<AdminProductGrantDto>> GetAllAsync(
        Guid? productId = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminProductGrantDto>> GetAsync(
        Guid grantId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminProductGrantDto>> CreateAsync(
        CreateProductGrantRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a grant's reference or quantity. It cannot be moved to another product — that would
    /// silently alter what two products hand over.
    /// </summary>
    Task<ServiceResult<AdminProductGrantDto>> UpdateAsync(
        Guid grantId,
        UpdateProductGrantRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteAsync(Guid grantId, CancellationToken cancellationToken = default);
}
