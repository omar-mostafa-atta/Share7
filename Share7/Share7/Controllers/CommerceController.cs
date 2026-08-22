using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Interfaces;

namespace Share7.API.Controllers;

/// <summary>
/// The player-facing shop surface. Everything here is scoped to the caller's own account.
/// <para>
/// Balances live at <c>/api/commerce/balances</c> on <c>CurrenciesController</c>, because they are
/// currency rather than commerce; the path is fixed by the Unity contract either way. Offers and
/// purchase are not built yet.
/// </para>
/// </summary>
[ApiController]
[Route("api/commerce")]
[Authorize]
public class CommerceController : ControllerBase
{
    private readonly IEntitlementService _entitlements;
    private readonly IOfferService _offers;
    private readonly IPurchaseService _purchases;
    private readonly ICurrentUserService _currentUser;

    public CommerceController(
        IEntitlementService entitlements,
        IOfferService offers,
        IPurchaseService purchases,
        ICurrentUserService currentUser)
    {
        _entitlements = entitlements;
        _offers = offers;
        _purchases = purchases;
        _currentUser = currentUser;
    }

    /// <summary>
    /// The shop, resolved for the caller and in their content language:
    /// <code>
    /// {
    ///   "offers": [ { "offerId": "…", "name": "…", "productIds": ["…"], "currency": "coins",
    ///                 "price": 100, "originalPrice": 150, "availability": "AVAILABLE",
    ///                 "canPurchase": true, "ineligibleReasonKey": null, "purchaseLimit": null,
    ///                 "purchaseCount": 0, "expiresAtUtc": null, "sortOrder": 0, "badgeKey": null } ],
    ///   "products": [ { "productId": "…", "grants": [ … ] } ]
    /// }
    /// </code>
    /// <para>
    /// **The backend decides everything here.** Price, currency, availability, whether it can be
    /// bought and why not are all resolved server-side; render the result and recompute none of it.
    /// </para>
    /// <para>
    /// `productIds` is a **list** — one offer can sell a bundle, and buying it grants every product
    /// in it. Their grants are under the top-level `products[]`, keyed by id, so a product appearing
    /// in several bundles is described once.
    /// </para>
    /// <para>
    /// `currency` is the stable **key** (`"coins"`), matching `GET /api/commerce/balances` — compare
    /// against that, not against `currencyId`. `availability` is one of `AVAILABLE`, `DISABLED`,
    /// `EXPIRED`, `PURCHASE_LIMIT_REACHED`; expiry and limits are evaluated per request against the
    /// server clock (`GET /api/time`) and this account's history.
    /// </para>
    /// <para>
    /// **Offers the caller cannot buy are still listed**, with `canPurchase: false` and a reason key,
    /// so the shop greys them out rather than having entries disappear. `canPurchase` deliberately
    /// ignores the balance — too few coins is a refusal at purchase time, not a hidden offer.
    /// </para>
    /// </summary>
    [HttpGet("offers")]
    public async Task<IActionResult> GetOffers(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        return Ok(await _offers.GetForUserAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Today's shelf: **only the offers this account can actually buy right now.** Same response
    /// shape as <c>GET /api/commerce/offers</c>, and the same auth — any signed-in player, not just
    /// an admin.
    /// <para>
    /// Excludes anything expired, switched off, or already bought to its per-account limit. It does
    /// **not** filter on affordability: an offer costing more than the caller holds still appears,
    /// because a student should see what they are saving towards, and the client already knows both
    /// numbers.
    /// </para>
    /// <para>
    /// Expiry is judged against the **server** clock (`GET /api/time`), never the device's. This is
    /// the endpoint to drive a "deals" screen from; use the unfiltered `GET /api/commerce/offers`
    /// when you want to render sold-out and expired entries greyed out instead of hiding them.
    /// </para>
    /// </summary>
    [HttpGet("offers/today")]
    public async Task<IActionResult> GetTodaysOffers(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        return Ok(await _offers.GetActiveForUserAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Buys an offer:
    /// <code>{ "offerId": "…", "requestId": "client-generated-unique-id" }</code>
    /// <para>
    /// **Atomic**: the currency debit, every entitlement, the ledger entries and the transaction row
    /// commit together or not at all. There is no outcome where an account is charged and not
    /// granted.
    /// </para>
    /// <para>
    /// **Idempotent**: `requestId` is the key. Retrying with the *same* one returns the original
    /// outcome — including the original refusal — and charges nothing further. A new id for the same
    /// intended purchase is a new purchase, so **reuse it on retry**.
    /// </para>
    /// <para>
    /// `200` completed, `409` refused with a `failureReasonKey`. Both are answers and both carry the
    /// authoritative `balances[]`, so no second call is needed to reconcile the wallet. Only `5xx`
    /// means the outcome is unknown — retry that with the same `requestId`.
    /// </para>
    /// </summary>
    /// <response code="404">No such offer.</response>
    /// <response code="409">`INSUFFICIENT_BALANCE`, `OFFER_UNAVAILABLE`, `OFFER_EXPIRED`, `PURCHASE_LIMIT_REACHED`, `ALREADY_OWNED`.</response>
    [HttpPost("purchase")]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Purchase(PurchaseRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _purchases.PurchaseAsync(userId, request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Everything the caller owns, newest first:
    /// <code>
    /// { "entitlements": [ { "entitlementId": "…", "productId": "…",
    ///                       "grantedAtUtc": "2026-08-12T18:00:00Z", "source": "PURCHASE" } ] }
    /// </code>
    /// <para>
    /// **Ownership outlives the shop.** Products that have been retired or delisted still appear —
    /// an entitlement is a permanent record, and what it grants stays resolvable through the
    /// product long after it stops being purchasable. Do not treat a `productId` missing from the
    /// current offer list as revoked.
    /// </para>
    /// <para>
    /// What each entitlement actually hands over is not repeated here. The client already has the
    /// grants from the offers response and from the purchase that created it, and re-sending them
    /// on every inventory read would duplicate the catalogue on the wire.
    /// </para>
    /// <para>
    /// `source` is a stable token — `PURCHASE` or `ADMIN_GRANT`. `grantedAtUtc` carries a trailing
    /// `Z`, unlike the older progress timestamps.
    /// </para>
    /// </summary>
    [HttpGet("entitlements")]
    public async Task<IActionResult> GetEntitlements(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var entitlements = await _entitlements.GetForUserAsync(userId, cancellationToken);
        return Ok(new EntitlementsResponse { Entitlements = entitlements });
    }
}
