using System.Text.RegularExpressions;

namespace Share7.Application.Commerce.Models;

/// <summary>
/// Normalises a <c>ProductKind.Name</c> into the <c>SCREAMING_SNAKE</c> token the client reads as a
/// grant's <c>kind</c>.
/// <para>
/// Kind used to be an enum, where <see cref="WireEnum"/> derived the wire form from the member name
/// and the vocabulary could not drift. It is admin-authored text now, so this is what keeps
/// <c>Cosmetic</c>, <c>cosmetic</c>, <c>COSMETIC</c> and <c>Content Pack</c> from reaching Unity as
/// four different tokens. It is also what kind names are compared on for uniqueness — two rows that
/// normalise the same would be indistinguishable to the client.
/// </para>
/// </summary>
public static class ProductKindName
{
    /// <summary>
    /// <c>"Content Pack"</c>, <c>"content-pack"</c> and <c>"ContentPack"</c> all become
    /// <c>"CONTENT_PACK"</c>. Non-Latin names — Arabic, say — have no case or word boundaries to
    /// work with and pass through as typed, so keep kind names ASCII.
    /// </summary>
    public static string ToWire(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Split camel/Pascal first, so ContentPack gains the boundary that "Content Pack" already has.
        var split = Regex.Replace(name.Trim(), "(?<=[a-z0-9])([A-Z])", "_$1");
        var underscored = Regex.Replace(split, @"[\s\-]+", "_");

        return Regex.Replace(underscored, "_{2,}", "_").Trim('_').ToUpperInvariant();
    }
}
