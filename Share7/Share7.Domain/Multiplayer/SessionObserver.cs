namespace Share7.Domain.Multiplayer;
public class GameObservationCapability
{
    public Guid GameId { get; set; }
    public int ProtocolVersion { get; set; }
    public int ContractVersion { get; set; } = 1;
    public int MaxObservers { get; set; } = 4;
    public bool Enabled { get; set; }
}
/// <summary>An expiring read-only transport membership, never a player seat.</summary>
public class SessionObserver
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
