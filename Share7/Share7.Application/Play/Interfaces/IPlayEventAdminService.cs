using Share7.Application.Common.Models;
using Share7.Application.Play.Models;

namespace Share7.Application.Play.Interfaces;

/// <summary>
/// Authoring competitions.
/// <para>
/// <b>Creating an event creates its ladder.</b> The board and its single cycle are written in the
/// same transaction as the event, because an event with no ladder has nowhere to rank and a ladder
/// with no event has nobody to pay. The window lives on the cycle from that moment on, and every
/// read of "is it open" goes through it.
/// </para>
/// </summary>
public interface IPlayEventAdminService
{
    Task<IReadOnlyList<PlayEventAdminDto>> ListForAuthoringAsync(
        Guid? gameId = null, bool includeFinished = true, CancellationToken cancellationToken = default);

    Task<PlayEventAdminDto?> GetForAuthoringAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PlayEventAdminDto>> CreateAsync(
        SavePlayEventRequest request, Guid createdByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an event. What may change narrows as the event progresses: before it opens almost
    /// everything is editable, once it is open the window may only be extended and the prize table is
    /// frozen, and once it has closed nothing but presentation moves — the entrants played under the
    /// rules they were shown.
    /// </summary>
    Task<ServiceResult<PlayEventAdminDto>> UpdateAsync(
        Guid eventId, SavePlayEventRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls an event off. Entries stop immediately, the ladder is closed, and **no prize is ever
    /// awarded** — including if the cycle later settles. Reversible only by authoring a new event.
    /// </summary>
    Task<ServiceResult<PlayEventAdminDto>> CancelAsync(
        Guid eventId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies an event into a new one starting after this one ends — the weekly-event workflow,
    /// which is otherwise a dozen fields retyped by hand every Monday.
    /// </summary>
    Task<ServiceResult<PlayEventAdminDto>> DuplicateAsync(
        Guid eventId, Guid createdByUserId, CancellationToken cancellationToken = default);

    /// <summary>What an event paid out, for the operator's own reconciliation.</summary>
    Task<IReadOnlyList<EventAwardDto>> GetAwardsAsync(
        Guid eventId, CancellationToken cancellationToken = default);
}

/// <summary>The real-world prize queue, which only a person can move.</summary>
public interface IPrizeClaimAdminService
{
    Task<IReadOnlyList<PrizeClaimAdminDto>> ListAsync(
        string? state = null, Guid? eventId = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<PrizeClaimAdminDto>> UpdateAsync(
        Guid claimId,
        UpdatePrizeClaimRequest request,
        Guid reviewedByUserId,
        CancellationToken cancellationToken = default);
}
