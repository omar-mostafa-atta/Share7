using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Asking someone into a room. Who may be asked is <c>ISocialPolicy</c>'s decision; whether they
/// then get a seat is the seat step's — an invite is permission to ask, never a seat.
/// </summary>
public interface ISessionInvitationService
{
    /// <summary>
    /// A seated player invites a classmate or friend into their room, before the match starts.
    /// Pressing twice, or two players inviting the same person, yields the one pending invite.
    /// Refusals: <c>SESSION_NOT_FOUND</c>, <c>NOT_SESSION_MEMBER</c> (no seat here),
    /// <c>SESSION_INVALID_TRANSITION</c> (started or ended), <c>SOCIAL_NOT_ALLOWED</c>,
    /// <c>SESSION_RESERVED</c>, <c>SESSION_REMOVED</c>, <c>VALIDATION_FAILED</c>.
    /// </summary>
    Task<ServiceResult<SessionInvitationDto>> InviteAsync(
        Guid userId, Guid sessionId, InvitePlayerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Invites waiting for the caller's answer — what to show after a reinstall.</summary>
    Task<ServiceResult<IReadOnlyList<SessionInvitationDto>>> PendingAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a seat in the room through the ordinary seat step. Accepting an invite already accepted,
    /// from a seat still held, answers with the room again.
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> AcceptAsync(
        Guid userId, Guid invitationId, AcceptInvitationRequest request, CancellationToken cancellationToken = default);

    /// <summary>The sender is not told — a declined invite simply expires on their side.</summary>
    Task<ServiceResult<SessionInvitationDto>> DeclineAsync(
        Guid userId, Guid invitationId, CancellationToken cancellationToken = default);

    Task<ServiceResult<SessionInvitationDto>> CancelAsync(
        Guid userId, Guid invitationId, CancellationToken cancellationToken = default);
}
