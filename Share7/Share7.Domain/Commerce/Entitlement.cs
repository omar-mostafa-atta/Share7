namespace Share7.Domain.Commerce;

/// <summary>
/// One account owns one product, permanently.
/// <para>
/// **The row records ownership, not what was owned.** What the account actually gets is resolved by
/// walking <c>Entitlement → Product → ProductGrant</c> on read. That is the shape the commerce
/// contract specifies, and it is why a product is retired rather than deleted and why its grants
/// stop being editable once anyone owns it — the chain has to stay walkable long after the product
/// has left the shop.
/// </para>
/// <para>
/// **Unique per (user, product).** Ownership is a boolean: an account either has the product or it
/// does not, and a second row would mean the same cosmetic appearing twice in the client's
/// inventory. A repeat purchase is refused with <c>ALREADY_OWNED</c> rather than granted again,
/// which also makes granting naturally idempotent — the index is what a retried purchase collides
/// with.
/// </para>
/// </summary>
public class Entitlement
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public DateTime GrantedAtUtc { get; set; }

    public EntitlementSource Source { get; set; }

    /// <summary>
    /// What caused it, in the source domain's terms — the purchase transaction, or the admin who
    /// granted it. Not a foreign key: like the ledger's, this reference has to outlive whatever it
    /// points at.
    /// </summary>
    public string? SourceId { get; set; }
}
