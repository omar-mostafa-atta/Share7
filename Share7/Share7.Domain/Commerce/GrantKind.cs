namespace Share7.Domain.Commerce;

/// <summary>
/// What kind of thing a <see cref="ProductGrant"/> hands over, and therefore how the **client**
/// should interpret its <see cref="ProductGrant.Reference"/>.
/// <para>
/// The backend never resolves a reference. It is an opaque, stable client identifier — a cosmetic
/// id, an Addressables pack id — deliberately, because building a backend cosmetic catalogue was
/// ruled out. This enum exists only so the client knows which of its own catalogues to look in.
/// </para>
/// <para>
/// Every kind here is **durable**: owning it is permanent. There is no consumable kind, and adding
/// one would need more than an enum value — an entitlement is a boolean "this account owns this
/// product", which is what makes it unique per (user, product).
/// </para>
/// </summary>
public enum GrantKind
{
    Unknown = 0,

    /// <summary>A cosmetic the client already knows about. <c>Reference</c> is its cosmetic id.</summary>
    Cosmetic,

    /// <summary>
    /// A content pack delivered by Unity Addressables/CDN. <c>Reference</c> is the pack id that
    /// <c>GET /api/content/manifest</c> will report as <c>packId</c>.
    /// <para>
    /// Usable now — the entitlement is granted and the reference preserved like any other — but
    /// nothing consumes it until the manifest is built.
    /// </para>
    /// </summary>
    ContentPack
}
