using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;

namespace Share7.Application.Commerce.Interfaces;

/// <summary>
/// Authoring for the product catalogue. Admin-only: a product defines what a purchase hands over,
/// so a player able to author one could grant themselves anything.
/// <para>
/// What a product hands over is not edited here — grants have their own service, because they are
/// their own table with their own lifecycle. This one owns identity, art and categorisation.
/// </para>
/// <para>
/// **Delete is refused while anyone owns it** (<c>PRODUCT_OWNED</c>): their entitlements resolve
/// through to it, so removing it would strand them. Retire with <c>active: false</c> instead, which
/// stops new grants and leaves every existing owner untouched. An unowned product deletes outright,
/// taking its grants with it.
/// </para>
/// </summary>
public interface IProductAdminService
{
    Task<IReadOnlyList<AdminProductDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminProductDto>> GetAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminProductDto>> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminProductDto>> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteAsync(Guid productId, CancellationToken cancellationToken = default);
}
