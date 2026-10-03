namespace Share7.Domain.Social;

/// <summary>A private consenting roster. No public name, chat or team-owned currency.</summary>
public class PlayerTeam
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string RequestId { get; set; } = "";
}
public class PlayerTeamMember
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public bool Accepted { get; set; }
    public DateTime InvitedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
}
