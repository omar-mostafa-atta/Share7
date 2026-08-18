using Share7.Domain.LookUps;

namespace Share7.Domain.Commerce;

/// <summary>
/// A product kind's display text in one language. Composite key (ProductKindId, LangId).
/// <para>
/// **This is not what the client matches on.** <see cref="ProductKind.Name"/> stays on the parent
/// row, untranslated, because it is normalised into the <c>kind</c> token every grant reports —
/// <c>COSMETIC</c> has to mean the same thing to an Arabic and an English client. This table is the
/// human label an admin reads, and it never reaches Unity.
/// </para>
/// </summary>
public class ProductKindTranslation : ILocalizedText
{
    public Guid ProductKindId { get; set; }
    public ProductKind? ProductKind { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    /// <summary>Required in **every** configured language.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
