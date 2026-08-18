using Share7.Application.Common.Models;
using Share7.Domain.Commerce;

namespace Share7.Infrastructure.Commerce;

/// <summary>
/// Whether an offer can be bought, and the tokens that say so.
/// <para>
/// <paramref name="Error"/> is null when it can. It carries the machine code the purchase endpoint
/// refuses with, so the shop listing and the purchase itself cannot disagree — a listing that says
/// <c>EXPIRED</c> and a purchase that succeeds anyway would be far worse than either alone.
/// </para>
/// </summary>
internal record OfferEligibility(
    string Availability,
    bool CanPurchase,
    string? ReasonKey,
    ApiErrorCode? Error);

internal static class OfferRules
{
    /// <summary>
    /// One evaluation, used by both <c>GET /api/commerce/offers</c> and the purchase endpoint.
    /// <para>
    /// Deliberately **does not look at the caller's balance.** Too few coins is a refusal at the
    /// moment of buying, not a reason to grey an offer out — a student should see what they are
    /// saving towards, and the client already knows the balance and the price.
    /// </para>
    /// </summary>
    public static OfferEligibility Evaluate(
        Offer offer,
        int purchaseCount,
        DateTime nowUtc,
        bool ownsEverything = false)
    {
        if (offer.Availability != OfferAvailability.Available)
            return Refused("DISABLED", ApiErrors.OfferUnavailable);

        // <=, not <: an offer expiring at 18:00 is not purchasable at 18:00.
        if (offer.ExpiresAtUtc is { } expiry && expiry <= nowUtc)
            return Refused("EXPIRED", ApiErrors.OfferExpired);

        // Null limit means unlimited. Only completed purchases are counted, so a refused attempt
        // never eats an allowance. Checked before ownership so that "you have had your two" stays
        // the reason when a limit is what actually stopped it.
        if (offer.PurchaseLimit is { } limit && purchaseCount >= limit)
            return Refused("PURCHASE_LIMIT_REACHED", ApiErrors.PurchaseLimitReached);

        // Entitlements are permanent and unique per (user, product), so an offer whose every product
        // the account already holds would hand over nothing. It has to be refused here as well as at
        // purchase time, or the shop advertises something the purchase endpoint then rejects — and a
        // listing that disagrees with the till is worse than either being wrong alone.
        if (ownsEverything)
            return Refused("NOT_ELIGIBLE", ApiErrors.AlreadyOwned);

        return new OfferEligibility("AVAILABLE", CanPurchase: true, ReasonKey: null, Error: null);
    }

    private static OfferEligibility Refused(string availability, ApiErrorCode error) =>
        new(availability, CanPurchase: false, error.MessageKey, error);
}
