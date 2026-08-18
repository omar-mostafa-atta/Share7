using System.ComponentModel.DataAnnotations;
using Share7.Application.Economy.Models;

namespace Share7.Application.Commerce.Models;

// ---- offers (client-facing) -------------------------------------------------------------------

/// <summary>
/// One shop entry. **Everything here is resolved by the backend for this caller at this moment** —
/// the client renders it and does not recompute any of it.
/// </summary>
public class OfferDto
{
    public Guid OfferId { get; init; }

    /// <summary>Shop text in the caller's content language.</summary>
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>
    /// Every product this offer sells — buying it grants **all** of them. Their grants are in the
    /// response's <c>products[]</c>, keyed by these ids, rather than repeated per offer.
    /// <para>
    /// Note this is a **list**: the commerce contract sketched a single <c>productId</c> per offer,
    /// which cannot express a bundle.
    /// </para>
    /// </summary>
    public IReadOnlyList<Guid> ProductIds { get; init; } = [];

    /// <summary>
    /// The currency **key** — <c>"coins"</c>. This is what to compare against
    /// <c>GET /api/commerce/balances</c>, which reports the same key. Not the row id.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>The row id, for calling back into the API. Balances never speak this.</summary>
    public Guid CurrencyId { get; init; }

    public long Price { get; init; }

    /// <summary>Pre-discount price for a struck-through display, or null when not discounted.</summary>
    public long? OriginalPrice { get; init; }

    /// <summary>
    /// Stable token: <c>AVAILABLE</c>, <c>DISABLED</c>, <c>EXPIRED</c>,
    /// <c>PURCHASE_LIMIT_REACHED</c>. Wider than what is stored — expiry and limits are resolved
    /// per request, against the server clock and this caller's history.
    /// </summary>
    public string Availability { get; init; } = string.Empty;

    /// <summary>
    /// The resolved answer, so the client does not re-derive it from the enum. False whenever
    /// <see cref="IneligibleReasonKey"/> is set.
    /// <para>
    /// **Does not consider the caller's balance.** Too few coins is a purchase-time refusal, not a
    /// reason to hide the offer — the client compares <see cref="Price"/> against the balance it
    /// already holds to decide how to render it.
    /// </para>
    /// </summary>
    public bool CanPurchase { get; init; }

    /// <summary>A Unity localization key when it cannot be bought, null when it can.</summary>
    public string? IneligibleReasonKey { get; init; }

    /// <summary>How many times one account may buy it. Null means unlimited.</summary>
    public int? PurchaseLimit { get; init; }

    /// <summary>How many times **this caller** already has. Only completed purchases count.</summary>
    public int PurchaseCount { get; init; }

    /// <summary>UTC with a trailing <c>Z</c>, or null when it never expires.</summary>
    public DateTime? ExpiresAtUtc { get; init; }

    public int SortOrder { get; init; }

    /// <summary>A token the client maps to its own badge art, e.g. <c>best_value</c>. Never text.</summary>
    public string? BadgeKey { get; init; }
}

/// <summary>
/// The shop. <c>products[]</c> is a flat lookup of everything the listed offers sell, so a product
/// appearing in three bundles is described once instead of three times.
/// </summary>
public class OffersResponse
{
    public IReadOnlyList<OfferDto> Offers { get; init; } = [];
    public IReadOnlyList<ProductDto> Products { get; init; } = [];
}

// ---- purchase -------------------------------------------------------------------------------

public class PurchaseRequest
{
    [Required]
    public Guid OfferId { get; set; }

    /// <summary>
    /// **Optional.** A client-generated id unique to this purchase attempt; the server generates one
    /// when it is omitted, so <c>{ "offerId": "…" }</c> alone is a valid request.
    /// <para>
    /// Supplying it is what makes a **retry** safe: replaying the same id returns the original
    /// outcome instead of buying again, which is the only protection against a request that timed
    /// out after the server had already charged. Omit it and every call is a fresh purchase.
    /// </para>
    /// <para>
    /// A double-tapped buy button is still safe either way — that is caught by refusing a purchase
    /// that would grant nothing new, not by this.
    /// </para>
    /// </summary>
    [MaxLength(128)]
    public string? RequestId { get; set; }
}

/// <summary>
/// The outcome. Returned with <c>200</c> for a completed purchase and <c>409</c> for a business
/// refusal — both are answers, and both carry the authoritative balances so the client can
/// reconcile without a second round trip. Only <c>5xx</c> means the outcome is unknown.
/// </summary>
public class PurchaseResponse
{
    /// <summary><c>COMPLETED</c> or <c>REFUSED</c>.</summary>
    public string State { get; init; } = string.Empty;

    public Guid? TransactionId { get; init; }

    /// <summary>Server time the transaction was recorded, UTC with a trailing <c>Z</c>.</summary>
    public DateTime? TransactionAtUtc { get; init; }

    public Guid OfferId { get; init; }

    /// <summary>What the offer sells. Empty on a refusal — nothing was granted.</summary>
    public IReadOnlyList<Guid> ProductIds { get; init; } = [];

    /// <summary>Those products and what each hands over, so the client can apply them immediately.</summary>
    public IReadOnlyList<ProductDto> Products { get; init; } = [];

    /// <summary>The entitlements now held for those products. Empty on a refusal.</summary>
    public IReadOnlyList<EntitlementDto> Entitlements { get; init; } = [];

    /// <summary>Absolute authoritative balances after the attempt — never deltas.</summary>
    public IReadOnlyList<BalanceDto> Balances { get; init; } = [];

    /// <summary>
    /// Why it was refused, as a Unity localization key. Null on success.
    /// <para>
    /// **True on a retry as well**: a replayed <c>requestId</c> returns the original transaction's
    /// state and reason, not a fresh evaluation.
    /// </para>
    /// </summary>
    public string? FailureReasonKey { get; init; }

    /// <summary>
    /// True when this response replayed an earlier transaction rather than performing a new one.
    /// Nothing was charged or granted on this call.
    /// </summary>
    public bool Replayed { get; init; }
}

// ---- offer authoring (admin) -----------------------------------------------------------------

public class CreateOfferRequest
{
    /// <summary>Shop name and description per language. All configured languages required.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one translation is required.")]
    public List<CommerceTranslationRequest> Translations { get; set; } = [];

    /// <summary>Which soft currency the price is in. Nothing here is real money.</summary>
    [Required]
    public Guid CurrencyId { get; set; }

    [Range(0, long.MaxValue)]
    public long Price { get; set; }

    /// <summary>Optional pre-discount price. Must exceed <see cref="Price"/> if supplied.</summary>
    public long? OriginalPrice { get; set; }

    /// <summary><c>AVAILABLE</c> or <c>UNAVAILABLE</c>. Any reasonable spelling accepted.</summary>
    [MaxLength(32)]
    public string Availability { get; set; } = "AVAILABLE";

    /// <summary>How many times one account may buy it. **Omit or null for unlimited.**</summary>
    [Range(1, int.MaxValue)]
    public int? PurchaseLimit { get; set; }

    /// <summary>
    /// UTC. Omit for an offer that never expires. Compare against <c>GET /api/time</c> when setting
    /// it — the server clock is what decides, not the admin's browser.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public int SortOrder { get; set; }

    [MaxLength(64)]
    public string? BadgeKey { get; set; }

    /// <summary>
    /// Everything the offer sells. At least one; buying grants all of them. The same product may
    /// appear in several offers.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "An offer must sell at least one product.")]
    public List<Guid> ProductIds { get; set; } = [];
}

/// <summary>
/// One product an offer sells, and **exactly what buying it hands over**. Spelled out rather than
/// counted so an admin can confirm a shop entry actually delivers what it claims before publishing
/// it — a bundle whose second product grants nothing looks identical to a working one otherwise.
/// </summary>
public class AdminOfferProductDto
{
    public Guid ProductId { get; init; }
    public string Key { get; init; } = string.Empty;

    /// <summary>The product's name in the caller's language.</summary>
    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;
    public bool Active { get; init; }

    /// <summary>How many grants it hands over — zero is worth seeing before putting it on sale.</summary>
    public int GrantCount { get; init; }

    /// <summary>
    /// The grants themselves: what the client will actually receive, reference by reference. Same
    /// shape the offers and purchase responses use.
    /// </summary>
    public IReadOnlyList<ProductGrantDto> Grants { get; init; } = [];
}

public class AdminOfferDto
{
    public Guid OfferId { get; init; }

    /// <summary>
    /// The offer's name **in the caller's language**, falling back to English and then to whatever
    /// translation exists. One resolved string rather than the whole set — the same rule the
    /// player-facing listing follows.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid CurrencyId { get; init; }
    public string Currency { get; init; } = string.Empty;

    public long Price { get; init; }
    public long? OriginalPrice { get; init; }

    /// <summary>As stored: <c>AVAILABLE</c> or <c>UNAVAILABLE</c>, not the computed client token.</summary>
    public string Availability { get; init; } = string.Empty;

    public int? PurchaseLimit { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public int SortOrder { get; init; }
    public string? BadgeKey { get; init; }

    public IReadOnlyList<AdminOfferProductDto> Products { get; init; } = [];

    /// <summary>Completed purchases across **all** accounts. Non-zero blocks deleting the offer.</summary>
    public int PurchaseCount { get; init; }

    /// <summary>True once the server clock has passed <see cref="ExpiresAtUtc"/>.</summary>
    public bool Expired { get; init; }

    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

// ---- server time -----------------------------------------------------------------------------

/// <summary>
/// The server's authoritative UTC clock, and nothing else. Exists so the client and the admin
/// console agree with the machine that actually decides whether an offer has expired.
/// </summary>
public class ServerTimeResponse
{
    public DateTime UtcNow { get; init; }
}
