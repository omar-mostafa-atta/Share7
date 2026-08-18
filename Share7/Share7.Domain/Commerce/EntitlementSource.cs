namespace Share7.Domain.Commerce;

/// <summary>
/// How an account came to own a product. Kept on the entitlement rather than inferred, so a support
/// question — "why does this player have this?" — is answerable from the row itself.
/// </summary>
public enum EntitlementSource
{
    Unknown = 0,

    /// <summary>Bought with soft currency. <c>SourceId</c> is the purchase transaction.</summary>
    Purchase,

    /// <summary>Handed over by an admin. <c>SourceId</c> is the admin's user id.</summary>
    AdminGrant
}
