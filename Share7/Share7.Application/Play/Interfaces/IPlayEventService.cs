using Share7.Application.Common.Models;
using Share7.Application.Play.Models;

namespace Share7.Application.Play.Interfaces;

/// <summary>
/// Events as an entrant reads them: what is running, what it pays, and what they have won.
/// </summary>
public interface IPlayEventService
{
    /// <summary>
    /// Live events, newest-first by the operator's sort order.
    /// </summary>
    /// <param name="includeScheduled">
    /// Include events that have not opened yet, so a home card can advertise one. Finished events
    /// are included for a short window after settlement so winners can still see the outcome.
    /// </param>
    Task<PlayEventsResponse> ListAsync(
        Guid userId,
        string? gameKey = null,
        bool includeScheduled = true,
        CancellationToken cancellationToken = default);

    /// <summary>One event, with this caller's own standing and entry count filled in.</summary>
    Task<ServiceResult<PlayEventDto>> GetAsync(
        Guid userId, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What this account has won, newest first. Unseen awards are what the client's "you won"
    /// surface is driven by.
    /// </summary>
    Task<IReadOnlyList<EventAwardDto>> GetAwardsAsync(
        Guid userId, bool unseenOnly = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an award as shown. Idempotent: the timestamp is set once and a second call changes
    /// nothing, so a client that retries does not move the moment the child first saw it.
    /// </summary>
    Task<ServiceResult> MarkAwardSeenAsync(
        Guid userId, Guid awardId, CancellationToken cancellationToken = default);
}
