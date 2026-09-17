using Share7.Application.Commerce.Models;

namespace Share7.Application.Commerce.Interfaces;

/// <summary>
/// The shop as one account sees it right now.
/// <para>
/// **Everything the client displays is decided here**: price, currency, availability, whether it can
/// be bought and why not. The client renders the result and recomputes none of it — which is the
/// whole point of a server-authoritative shop.
/// </para>
/// </summary>
public interface IOfferService
{
    /// <summary>
    /// Every offer, in sort order, resolved for <paramref name="userId"/> — including ones it cannot
    /// buy, so the client can grey them out rather than have entries disappear.
    /// <para>
    /// Names come back in the caller's content language.
    /// </para>
    /// </summary>
    Task<OffersResponse> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Only what this account can actually buy right now — the shop's "today" shelf.
    /// <para>
    /// The complement of <see cref="GetForUserAsync"/>, which lists everything so the client can grey
    /// the rest out. Here the rejects are simply absent, so a caller that wants a clean list does not
    /// have to filter <c>canPurchase</c> itself and cannot get it subtly wrong.
    /// </para>
    /// <para>
    /// Filtered on the same evaluation the purchase endpoint uses, so anything returned here is
    /// buyable **except** for the balance — affordability is still decided at purchase time.
    /// </para>
    /// </summary>
    Task<OffersResponse> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
