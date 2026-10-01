using Share7.Application.Social;
using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

public class PartyDto
{
    public Guid Id { get; set; }
    public Guid LeaderUserId { get; set; }
    public PartyState State { get; set; }
    public int MaxSize { get; set; }
    public List<PartyMemberDto> Members { get; set; } = [];

    /// <summary>The room the leader last opened for the party, while it is live. Join it by id.</summary>
    public Guid? CurrentSessionId { get; set; }

    public DateTime ServerTimeUtc { get; set; }
}

public class PartyMemberDto
{
    public Guid UserId { get; set; }

    /// <summary>The public handle.</summary>
    public string? DisplayName { get; set; }

    public bool IsLeader { get; set; }
    public PlayerPresenceState Presence { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}

public class PartyInvitationDto
{
    public Guid Id { get; set; }
    public Guid PartyId { get; set; }
    public Guid FromUserId { get; set; }
    public string? FromDisplayName { get; set; }
    public Guid ToUserId { get; set; }
    public InvitationState State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public class PartyPlayRequest : MultiplayerRequest
{
    public Guid GameId { get; set; }
    public string? ModeKey { get; set; }
    public CurriculumPathDto? CurriculumPath { get; set; }

    /// <summary>The Photon room the leader will bring up.</summary>
    public string TransportSessionName { get; set; } = string.Empty;
    public string? TransportRegion { get; set; }
    public int ProtocolVersion { get; set; }
}
