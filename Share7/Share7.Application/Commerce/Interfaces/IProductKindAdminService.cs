using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;

namespace Share7.Application.Commerce.Interfaces;

/// <summary>
/// Authoring for product categories. Admin-only, like everything else that defines what a purchase
/// hands over.
/// <para>
/// A kind's name is **contract with the client**: it is normalised and sent as each grant's
/// <c>kind</c>, and there is no catalogue on this side to check it against. Renaming one changes
/// what every product of that kind reports.
/// </para>
/// </summary>
public interface IProductKindAdminService
{
    Task<IReadOnlyList<ProductKindDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<ProductKindDto>> GetAsync(
        Guid productKindId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProductKindDto>> CreateAsync(
        CreateProductKindRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ProductKindDto>> UpdateAsync(
        Guid productKindId,
        UpdateProductKindRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refused with <c>PRODUCT_KIND_IN_USE</c> while any product references it — those products
    /// would lose the one thing telling the client how to read their grants.
    /// </summary>
    Task<ServiceResult> DeleteAsync(Guid productKindId, CancellationToken cancellationToken = default);
}
