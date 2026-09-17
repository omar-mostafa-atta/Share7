using Share7.Domain.LookUps;

namespace Share7.Domain.Commerce;

/// <summary>
/// A product's shop text in one language. Composite key (ProductId, LangId).
/// <para>
/// Same shape as the curriculum's <c>*Translations</c> tables and for the same reason: one product
/// has one id in every language, so entitlements and offers survive a language switch. The product
/// row itself carries no display text at all — <see cref="Product.Key"/> is its only human-readable
/// handle, and that one is machine-facing.
/// </para>
/// </summary>
public class ProductTranslation : ILocalizedText
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    /// <summary>Required in **every** configured language — a product is never half-translated.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional per language, unlike the name.</summary>
    public string? Description { get; set; }
}
