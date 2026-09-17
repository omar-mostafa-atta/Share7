using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;

namespace Share7.Application.Commerce.Interfaces;

/// <summary>
/// Authoring the shop. Admin-only: an offer decides what something costs, so a player able to write
/// one could price anything at zero.
/// <para>
/// **Deleting is refused once an offer has sold** (<c>OFFER_PURCHASED</c>) — the transaction history
/// points at it and has to stay readable. Switch it to <c>UNAVAILABLE</c> instead, which takes it
/// off sale and leaves every past purchase intact.
/// </para>
/// </summary>
public interface IOfferAdminService
{
    Task<IReadOnlyList<AdminOfferDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminOfferDto>> GetAsync(Guid offerId, CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminOfferDto>> CreateAsync(
        CreateOfferRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// **There is no update.** Offers are authored once and retired by deletion, by request — see
    /// the note on <see cref="DeleteAsync"/> for what that means for an offer that has already been
    /// transacted against.
    /// </summary>
    Task<ServiceResult> DeleteAsync(Guid offerId, CancellationToken cancellationToken = default);
}
