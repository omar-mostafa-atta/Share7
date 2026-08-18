namespace Share7.Domain.Commerce;

/// <summary>
/// A sellable thing, defined by what it hands over rather than by a price. **Price lives on the
/// Offer, not here** — the same product can be sold at different prices, in different currencies,
/// at different times, and an account that already owns it keeps it when every offer is gone.
/// <para>
/// **Deleting one is refused while anybody owns it.** An <see cref="Entitlement"/> resolves what it
/// owns by walking <c>Entitlement → Product → ProductGrant</c>, so deleting an owned product would
/// strand every account that bought it — which is exactly the failure the commerce contract calls
/// out. Retire it with <see cref="Active"/> = false instead; an unowned product can be deleted
/// outright, and its grants go with it.
/// </para>
/// </summary>
public class Product
{
    public Guid Id { get; set; }

    /// <summary>
    /// Takes the product out of circulation without destroying ownership. New entitlements are
    /// refused; existing ones stay valid and keep resolving their grants.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// Where the shop art lives. A plain URL the backend stores and hands back — it does not host,
    /// fetch or validate the image, in the same spirit as content delivery staying with Unity
    /// Addressables/CDN.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Stable identifier for admin tooling and seed data, e.g. <c>"skin_astronaut"</c>. Unique and
    /// permanent.
    /// <para>
    /// Unlike <c>Currency.Key</c> this is **not** what the client speaks — the commerce contract
    /// puts <c>productId</c> on the wire everywhere. It exists so that a product can be referred to
    /// in configuration and migrations without hard-coding a GUID.
    /// </para>
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// What category of thing this is. Required — the kind is what tells the client which of its own
    /// catalogues to resolve the grants against, so a product without one grants things nobody can
    /// look up.
    /// </summary>
    public Guid ProductKindId { get; set; }

    public ProductKind? Kind { get; set; }

    /// <summary>
    /// Name and description, one row per language. The product carries no display text of its own —
    /// a name is required for **every** configured language, so it can never be half-translated.
    /// </summary>
    public ICollection<ProductTranslation> Translations { get; set; } = new List<ProductTranslation>();

    public ICollection<ProductGrant> Grants { get; set; } = new List<ProductGrant>();
}
