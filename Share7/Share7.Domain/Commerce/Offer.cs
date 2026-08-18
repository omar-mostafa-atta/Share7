using Share7.Domain.Economy;

namespace Share7.Domain.Commerce;

/// <summary>
/// What a product costs, when, and to whom. **The product says what is handed over; the offer says
/// what it takes to get it** — which is why the same product can be sold at two prices at once, and
/// why an account keeps what it bought after every offer for it is gone.
/// <para>
/// One offer can sell **several products together** (<see cref="Products"/>): a bundle is one
/// purchase, one price and one transaction that grants every product in it.
/// </para>
/// <para>
/// Nothing here is real money. <see cref="CurrencyId"/> always points at a soft currency.
/// </para>
/// </summary>
public class Offer
{
    public Guid Id { get; set; }

    /// <summary>
    /// What it costs, in <see cref="CurrencyId"/>. Whole units, never fractional — the same rule the
    /// wallet follows.
    /// </summary>
    public long Price { get; set; }

    /// <summary>
    /// The pre-discount price, for a struck-through display. Null when the offer is not a discount;
    /// the backend never derives one from it, it is purely something to render.
    /// </summary>
    public long? OriginalPrice { get; set; }

    public Guid CurrencyId { get; set; }
    public Currency? Currency { get; set; }

    /// <summary>
    /// The admin's switch, and **only** the admin's switch. What the client finally sees under
    /// <c>availability</c> is computed per request — an offer that is <see cref="OfferAvailability.Available"/>
    /// here still reports <c>EXPIRED</c> once its date passes, or <c>PURCHASE_LIMIT_REACHED</c> to
    /// an account that has had its fill.
    /// </summary>
    public OfferAvailability Availability { get; set; } = OfferAvailability.Available;

    /// <summary>
    /// How many times one account may buy this. **Null means unlimited.** Counted against completed
    /// transactions, so a refused purchase never consumes an allowance.
    /// </summary>
    public int? PurchaseLimit { get; set; }

    /// <summary>
    /// When it stops being purchasable. Null means it never expires. Compared against the server
    /// clock, never the client's — which is what <c>GET /api/time</c> exists to let the client
    /// agree with.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Shop ordering, ascending. The backend owns the order, not the client.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// A stable token the client maps to its own badge art — <c>best_value</c>, <c>new</c>. Not
    /// display text: like every other key here, Unity owns the pixels and the words.
    /// <para>Nothing consumes it yet; it is stored and returned so the shape does not change later.</para>
    /// </summary>
    public string? BadgeKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Name and description per language. Required in every configured language.</summary>
    public ICollection<OfferTranslation> Translations { get; set; } = new List<OfferTranslation>();

    /// <summary>
    /// Everything this offer sells. Buying it grants **all** of them — there is no partial purchase,
    /// the same way a product hands over all of its grants.
    /// </summary>
    public ICollection<OfferProduct> Products { get; set; } = new List<OfferProduct>();
}
