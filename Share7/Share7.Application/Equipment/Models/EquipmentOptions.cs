namespace Share7.Application.Equipment.Models;

/// <summary>Configuration for the equipment endpoint, bound from the <c>Equipment</c> section.</summary>
public class EquipmentOptions
{
    public const string SectionName = "Equipment";

    /// <summary>
    /// Whether a save is rejected for equipping a cosmetic the account has no entitlement to.
    /// <para>
    /// **Off by default, deliberately, even though entitlements are wired.** The ownership check
    /// itself is built and tested — it resolves <c>Entitlement → Product → ProductGrant.Reference</c>,
    /// which is the client-side cosmetic id. What is missing is any notion of a cosmetic a player
    /// owns *without* an entitlement: starter outfits, defaults, anything granted by the client
    /// rather than bought. Nothing in the schema records those, so switching this on today would
    /// answer <c>422</c> to every player wearing a default and leave them unable to save at all.
    /// </para>
    /// <para>
    /// **Before turning it on**, one of these has to be true: every default cosmetic is granted as
    /// a real entitlement at account creation, or a free-cosmetic allowlist exists for the check to
    /// consult. Flip it here once it is — no code change needed.
    /// </para>
    /// </summary>
    public bool EnforceOwnership { get; set; }
}
