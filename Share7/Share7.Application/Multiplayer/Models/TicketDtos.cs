using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

public class EnqueueTicketRequest : MultiplayerRequest
{
    public Guid GameId { get; set; }
    public string? ModeKey { get; set; }
    public Guid? EventId { get; set; }

    /// <summary>Rated play: matched by skill, given a seasonal rank. Solo only, and only in a ranked mode.</summary>
    public bool Ranked { get; set; }

    public int ProtocolVersion { get; set; }

    /// <summary>A lesson, a subject, or neither — exactly as matchmaking takes it.</summary>
    public CurriculumPathDto? CurriculumPath { get; set; }

    /// <summary>The transport region the caller prefers, used if they end up hosting.</summary>
    public string? TransportRegion { get; set; }

    /// <summary>Queue the caller's whole party (the caller must lead it). Casual only.</summary>
    public Guid? PartyId { get; set; }
}

public class MatchmakingTicketDto
{
    public Guid Id { get; set; }
    public TicketState State { get; set; }
    public bool Ranked { get; set; }
    public Guid GameId { get; set; }
    public Guid ModeId { get; set; }
    public Guid? PartyId { get; set; }
    public List<Guid> Players { get; set; } = [];

    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set once matched: the session every player on the ticket is already seated in.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Who brings the transport room up. If it is you: create it and <c>start</c> the session.</summary>
    public Guid? HostUserId { get; set; }

    /// <summary>The room to create (host) or join (everyone else), once matched.</summary>
    public string? TransportSessionName { get; set; }
    public string? TransportRegion { get; set; }

    public string? EndReason { get; set; }
    public DateTime ServerTimeUtc { get; set; }
}
