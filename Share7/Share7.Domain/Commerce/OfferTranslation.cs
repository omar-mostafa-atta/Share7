using Share7.Domain.LookUps;

namespace Share7.Domain.Commerce;

/// <summary>
/// An offer's shop text in one language. Composite key (OfferId, LangId), same shape as
/// <see cref="ProductTranslation"/> and the curriculum tree.
/// <para>
/// Unlike a product's, **this text does reach the client**: the offers response returns the name and
/// description in the caller's language, because an offer is the thing a student reads in the shop
/// and there is no other source for "50% off, this week only".
/// </para>
/// </summary>
public class OfferTranslation : ILocalizedText
{
    public Guid OfferId { get; set; }
    public Offer? Offer { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    /// <summary>Required in **every** configured language — an offer is never half-translated.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
