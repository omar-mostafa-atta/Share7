using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

public class SessionInvitationDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid GameId { get; set; }
    public Guid? ModeId { get; set; }

    public Guid FromUserId { get; set; }

    /// <summary>The sender's public handle.</summary>
    public string? FromDisplayName { get; set; }

    public Guid ToUserId { get; set; }

    public InvitationState State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}

public class InvitePlayerRequest : MultiplayerRequest
{
    /// <summary>Who to invite — a user id from the caller's connections list.</summary>
    public Guid UserId { get; set; }
}

public class AcceptInvitationRequest : MultiplayerRequest
{
    public int ProtocolVersion { get; set; }
}
