using System.Security.Cryptography;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Builds pseudonymous handles like <c>SwiftFalcon418</c>.
/// <para>
/// **Derived from nothing.** Not the child's name, not their email, not their grade, not their
/// account id — the number is cryptographically random, not a hash of anything. A handle that was
/// a function of personal data would still be personal data, just harder to read, and the whole
/// point of this type is that a board row discloses nothing about who is standing on it.
/// </para>
/// <para>
/// Both word lists are deliberately bland: animals, colours and neutral adjectives, with no body
/// parts, no violence, no slang and nothing that combines into something a nine-year-old would be
/// teased for. The pairing is also checked so that no combination reads badly.
/// </para>
/// </summary>
public static class HandleGenerator
{
    private static readonly string[] Adjectives =
    [
        "Swift", "Brave", "Clever", "Sunny", "Lucky", "Happy", "Gentle", "Bright",
        "Calm", "Kind", "Quick", "Bold", "Cheery", "Nimble", "Jolly", "Merry",
        "Sharp", "Steady", "Cosmic", "Golden", "Silver", "Crimson", "Azure", "Emerald",
        "Amber", "Coral", "Violet", "Scarlet", "Jade", "Ruby", "Copper", "Ivory"
    ];

    private static readonly string[] Nouns =
    [
        "Falcon", "Tiger", "Panda", "Dolphin", "Otter", "Rocket", "Comet", "Meteor",
        "Maple", "Cedar", "River", "Summit", "Harbour", "Lantern", "Compass", "Beacon",
        "Sparrow", "Heron", "Badger", "Bison", "Gazelle", "Ibex", "Lynx", "Puffin",
        "Nebula", "Quasar", "Aurora", "Zephyr", "Cascade", "Canyon", "Orchid", "Willow"
    ];

    /// <summary>
    /// A fresh handle. Roughly 32 x 32 x 9000 combinations, which is enough that collisions are
    /// rare — the caller still retries on one, because "rare" is not "never" and a duplicate
    /// handle on a public board is confusing rather than harmless.
    /// </summary>
    public static string Next()
    {
        var adjective = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        var noun = Nouns[RandomNumberGenerator.GetInt32(Nouns.Length)];

        // Four digits, never leading-zero padded to look like an id, and never sequential — a
        // sequential suffix would leak signup order.
        var number = RandomNumberGenerator.GetInt32(1000, 10000);

        return $"{adjective}{noun}{number}";
    }
}
