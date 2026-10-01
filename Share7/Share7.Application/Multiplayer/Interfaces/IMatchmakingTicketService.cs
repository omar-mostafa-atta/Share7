using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Queued matchmaking: ranked (solo, matched by rating) and parties (placed as one). The answer
/// arrives later, on the player feed (<c>multiplayer.matchmaking.match_found</c>).
/// </summary>
public interface IMatchmakingTicketService
{
    /// <summary>
    /// Queues the caller — or, with a party id, the caller's whole party. Queueing again while a ticket
    /// is searching returns that ticket. Refusals: <c>ALREADY_IN_SESSION</c>, <c>PARTY_MEMBER_BUSY</c>,
    /// <c>NOT_PARTY_LEADER</c>, <c>RANKED_SOLO_ONLY</c>, <c>MODE_NOT_RANKED</c>,
    /// <c>PROTOCOL_VERSION_MISMATCH</c>, <c>PC_NO_SHARED_LESSON</c>, and the mode's own <c>PC_*</c>.
    /// </summary>
    Task<ServiceResult<MatchmakingTicketDto>> EnqueueAsync(Guid userId, EnqueueTicketRequest request, CancellationToken cancellationToken = default);

    /// <summary>The caller's searching ticket, or the one matched in the last few minutes; <c>TICKET_NOT_FOUND</c> otherwise.</summary>
    Task<ServiceResult<MatchmakingTicketDto>> CurrentAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Stops searching. Any player on the ticket may. A matched ticket cannot be cancelled — leave the session.</summary>
    Task<ServiceResult<MatchmakingTicketDto>> CancelAsync(Guid userId, Guid ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One worker pass: expire, put back the players of matches whose host never came, and form new
    /// matches. Runs under a platform-wide lock — a second instance's pass does nothing. Returns the
    /// number of matches formed.
    /// </summary>
    Task<int> FormMatchesAsync(CancellationToken cancellationToken = default);
}
