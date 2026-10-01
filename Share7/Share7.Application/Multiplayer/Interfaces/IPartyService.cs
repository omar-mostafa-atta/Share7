using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Parties: a few classmates or friends who stay together between matches. The leader invites
/// (through <c>ISocialPolicy</c>), picks what to play, and "play" opens a room reserved for exactly
/// the party. Queueing a party into public matchmaking needs tickets and is not offered.
/// </summary>
public interface IPartyService
{
    /// <summary>The caller's party, created with them as leader if they have none.</summary>
    Task<ServiceResult<PartyDto>> CreateAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The caller's party, or <c>PARTY_NOT_FOUND</c>.</summary>
    Task<ServiceResult<PartyDto>> CurrentAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The leader invites a classmate or friend. One pending invite per party and player.</summary>
    Task<ServiceResult<PartyInvitationDto>> InviteAsync(Guid userId, Guid partyId, InvitePlayerRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PartyInvitationDto>>> PendingInvitesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Joins the party — leaving any party the caller was in. <c>PARTY_FULL</c> when there is no room.</summary>
    Task<ServiceResult<PartyDto>> AcceptInviteAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>The leader is not told.</summary>
    Task<ServiceResult<PartyInvitationDto>> DeclineInviteAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>Leaves. A leader leaving hands the party to the longest-standing member; the last one out ends it.</summary>
    Task<ServiceResult> LeaveAsync(Guid userId, Guid partyId, CancellationToken cancellationToken = default);

    /// <summary>The leader removes a member.</summary>
    Task<ServiceResult<PartyDto>> RemoveAsync(Guid userId, Guid partyId, Guid memberUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The leader opens a private room reserved for the party; every other member is told to come in
    /// (<c>multiplayer.party.play_started</c>). Refuses <c>PARTY_MEMBER_BUSY</c> — with the members —
    /// when anyone is still in a room.
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> PlayAsync(Guid userId, Guid partyId, PartyPlayRequest request, CancellationToken cancellationToken = default);
}
