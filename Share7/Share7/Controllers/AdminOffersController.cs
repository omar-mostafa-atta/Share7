using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// The shop's price list. **Admin only** — an offer decides what something costs, so a player able
/// to author one could price anything at zero.
/// <para>
/// An offer sells one or more **products**, which are what actually gets handed over. The split is
/// deliberate: the same product can be sold at two prices at once, and an account keeps what it
/// bought long after every offer for it is gone.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/offers")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminOffersController : ControllerBase
{
    private readonly IOfferAdminService _offers;

    public AdminOffersController(IOfferAdminService offers)
    {
        _offers = offers;
    }

    /// <summary>
    /// Every offer in sort order, unavailable and expired ones included. <c>purchaseCount</c> is
    /// completed purchases across all accounts, and <c>expired</c> is measured against the server
    /// clock — both are why a delete would be refused, visible before attempting it.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var offers = await _offers.GetAllAsync(cancellationToken);
        return Ok(new { offers });
    }

    [HttpGet("{offerId:guid}")]
    public async Task<IActionResult> Get(Guid offerId, CancellationToken cancellationToken)
    {
        var result = await _offers.GetAsync(offerId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Puts something on sale:
    /// <code>
    /// {
    ///   "currencyId": "…",
    ///   "price": 100,
    ///   "originalPrice": 150,
    ///   "availability": "AVAILABLE",
    ///   "purchaseLimit": null,
    ///   "expiresAtUtc": null,
    ///   "sortOrder": 0,
    ///   "badgeKey": null,
    ///   "productIds": ["…", "…"],
    ///   "translations": [
    ///     { "langId": "…en", "name": "Starter bundle", "description": "Two skins, half price." },
    ///     { "langId": "…ar", "name": "حزمة البداية",   "description": "زيّان بنصف السعر." }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// <c>productIds</c> is a **list**: an offer can sell a bundle, and buying it grants every
    /// product in it as one purchase, one price and one transaction. At least one is required.
    /// </para>
    /// <para>
    /// <c>purchaseLimit</c> is **per account** — omit or null for unlimited, and only completed
    /// purchases count against it. <c>expiresAtUtc</c> is compared against the server clock, so set
    /// it from <c>GET /api/time</c> rather than the browser's. <c>originalPrice</c> must exceed
    /// <c>price</c> or be omitted. A name is required in every configured language.
    /// </para>
    /// </summary>
    /// <response code="400"><c>OFFER_INVALID</c> — bad availability, negative price, an originalPrice below price, or a missing translation.</response>
    /// <response code="404">No such currency or product.</response>
    [HttpPost]
    public async Task<IActionResult> Create(CreateOfferRequest request, CancellationToken cancellationToken)
    {
        var result = await _offers.CreateAsync(request, cancellationToken);

        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { offerId = result.Value!.OfferId }, result.Value)
            : result.ToApiErrorResult();
    }

    /// <summary>
    /// Deletes an offer nothing has ever transacted against. **Idempotent.**
    /// <para>
    /// **Refused with <c>OFFER_PURCHASED</c> (409)** once any transaction references it, refusals
    /// included — the history points at it and has to stay readable.
    /// </para>
    /// <para>
    /// ⚠ **There is no update endpoint**, by request. So once an offer has been transacted against —
    /// even by a single refused attempt — it can no longer be deleted, re-priced, or switched to
    /// <c>UNAVAILABLE</c>. It stays on sale exactly as authored. Author offers with an
    /// <c>expiresAtUtc</c> if they need to come off sale, since expiry is the only remaining way an
    /// offer stops being purchasable.
    /// </para>
    /// </summary>
    [HttpDelete("{offerId:guid}")]
    public async Task<IActionResult> Delete(Guid offerId, CancellationToken cancellationToken)
    {
        var result = await _offers.DeleteAsync(offerId, cancellationToken);
        return result.Succeeded ? NoContent() : result.ToApiErrorResult();
    }
}
