using Share7.Application.Commerce.Models;
using Share7.Domain.Commerce;

namespace Share7.Infrastructure.Commerce;

/// <summary>
/// Shared grant mapping, so the product, grant, entitlement and (later) offer responses all describe
/// a grant identically rather than growing four versions that drift.
/// <para>
/// Every mapper takes the kind name separately: it lives on the product now, not on the grant, and
/// passing it in keeps these functions from having to guess whether a navigation was loaded.
/// </para>
/// </summary>
internal static class CommerceMappings
{
    public static AdminProductGrantDto ToAdminDto(ProductGrant grant, string kindName) => new()
    {
        GrantId = grant.Id,
        ProductId = grant.ProductId,
        Kind = ProductKindName.ToWire(kindName),
        Reference = grant.Reference,
        Quantity = grant.Quantity
    };

    /// <summary>Ordered so repeated reads are byte-identical.</summary>
    public static IReadOnlyList<AdminProductGrantDto> ToAdminDtos(
        IEnumerable<ProductGrant> grants,
        string kindName) =>
        grants
            .OrderBy(g => g.Reference, StringComparer.Ordinal)
            .Select(grant => ToAdminDto(grant, kindName))
            .ToList();

    /// <summary>
    /// The client-facing shape: no grant id, because the contract addresses grants by what they
    /// hand over rather than by row. Used by the offers and purchase responses.
    /// </summary>
    public static IReadOnlyList<ProductGrantDto> ToClientDtos(
        IEnumerable<ProductGrant> grants,
        string kindName)
    {
        var kind = ProductKindName.ToWire(kindName);

        return grants
            .OrderBy(g => g.Reference, StringComparer.Ordinal)
            .Select(grant => new ProductGrantDto
            {
                Kind = kind,
                Reference = grant.Reference,
                Quantity = grant.Quantity
            })
            .ToList();
    }
}
