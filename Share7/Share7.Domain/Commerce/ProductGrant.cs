namespace Share7.Domain.Commerce;

/// <summary>
/// One thing a <see cref="Product"/> hands over. A product with three of these grants all three
/// together — buying it is a single act, and there is no partial ownership.
/// <para>
/// This is the table a purchase reads: resolve the offer to its product, select every grant row
/// carrying that <see cref="ProductId"/>, and hand the account all of them. Nothing else decides
/// what a purchase delivers.
/// </para>
/// <para>
/// There is no <c>Kind</c> here any more — the category lives on the product as
/// <see cref="Product.ProductKindId"/>, so every grant of one product is the same kind of thing.
/// </para>
/// </summary>
public class ProductGrant
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>
    /// An opaque, stable **client** identifier — a cosmetic id, a content pack id. The backend
    /// stores it and hands it back; it never looks it up, because there is no backend cosmetic
    /// catalogue by decision.
    /// <para>
    /// That makes this string the whole contract. It must stay stable on the client for as long as
    /// anyone owns a product referencing it: changing a cosmetic id orphans every entitlement
    /// pointing at it, and the backend cannot detect that having no catalogue to check against.
    /// </para>
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// How many. Almost always 1 for a cosmetic — it exists because the contract's grant shape has
    /// it, and because a pack granting several of something is a natural next step.
    /// </summary>
    public int Quantity { get; set; } = 1;
}
