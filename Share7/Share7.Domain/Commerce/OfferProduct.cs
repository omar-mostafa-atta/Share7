namespace Share7.Domain.Commerce;

/// <summary>
/// One product an offer sells. Composite key (OfferId, ProductId) — a bundle lists each product
/// once, and buying it grants every one of them.
/// <para>
/// **Restrict towards the product**, like <c>Entitlement</c>: a product being sold cannot be deleted
/// out from under the offer. Cascade towards the offer, because the link means nothing without it.
/// </para>
/// </summary>
public class OfferProduct
{
    public Guid OfferId { get; set; }
    public Offer? Offer { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}
