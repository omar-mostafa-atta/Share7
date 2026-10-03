namespace Share7.Domain.Multiplayer;

/// <summary>Minimal historical shell. No join code, transport name, lesson payload or roster.</summary>
public class SessionArchive
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid? ModeId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public DateTime ArchivedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public MultiplayerSessionState State { get; set; }
    public int ParticipantCount { get; set; }
}

public class SessionArchiveParticipant
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public int? Placement { get; set; }
    public bool IsWinner { get; set; }
    public bool Forfeited { get; set; }
}
