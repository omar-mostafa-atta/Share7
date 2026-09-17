namespace Share7.Domain.Play;

/// <summary>
/// The profiles the platform seeds, by key. Not an enum: profiles are rows an operator can add, and
/// only these four are referenced by name in code — as the fallback, and as what the seeder writes.
/// </summary>
public static class EconomyProfileKeys
{
    /// <summary>Pays exactly what the valuations say. What every mode uses unless told otherwise.</summary>
    public const string Default = "default";

    /// <summary>Event sessions. Seeded at the default rate, so an operator tunes events without touching modes.</summary>
    public const string Event = "event";

    /// <summary>Half pay, for a mode that would otherwise be the cheapest way to farm.</summary>
    public const string Reduced = "reduced";

    /// <summary>Pays nothing at all, rules included. The profile a purely-for-fun mode settles under.</summary>
    public const string None = "none";
}
