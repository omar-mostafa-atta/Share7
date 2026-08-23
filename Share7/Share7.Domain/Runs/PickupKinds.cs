using System.Text.RegularExpressions;

namespace Share7.Domain.Runs;

/// <summary>
/// What a pickup kind is allowed to look like: <c>coin</c>, <c>chest_small</c>,
/// <c>gem</c>, <c>mg147_starfish</c>.
/// <para>
/// **A free token rather than an enum, and the reasoning is the opposite of
/// <see cref="Rewards.RewardEventType"/>'s.** An event type must be an enum because the *server*
/// raises it, so a value with no producer is dead configuration. A pickup kind is raised by the
/// *client* and only ever priced by the server — an enum here would mean every mini-game's bespoke
/// collectible needs a backend migration and a deploy before it can be worth anything, which is
/// precisely the coupling this feature exists to remove.
/// </para>
/// <para>
/// It is also what makes an unpriced kind expressible at all. A kind the valuation table has never
/// heard of pays zero and does not fail the run; an enum would refuse it at model binding and lose a
/// child's whole run over a design oversight.
/// </para>
/// </summary>
public static class PickupKinds
{
    public const int MaxLength = 32;

    /// <summary>The runner's coin. The only kind with a producer today.</summary>
    public const string Coin = "coin";

    private static readonly Regex Shape = new(
        "^[a-z][a-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Same shape as a currency key, for the same reason: it is a stable identifier that appears in
    /// both a client prefab and a database row, and case-folding or punctuation differences between
    /// the two are indistinguishable from a kind that was never priced.
    /// </summary>
    public static bool IsValid(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && kind.Length <= MaxLength && Shape.IsMatch(kind);

    /// <summary>
    /// Trims and lowercases so <c>"Coin"</c> and <c>"coin"</c> resolve to the same valuation row.
    /// Returns null for anything that is not a legal token.
    /// </summary>
    public static string? Normalise(string? kind)
    {
        var trimmed = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return IsValid(trimmed) ? trimmed : null;
    }
}
