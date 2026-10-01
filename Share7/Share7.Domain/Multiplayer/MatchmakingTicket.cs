namespace Share7.Domain.Multiplayer;

/// <summary>
/// "Find me a match", held by the server until it can form a good one — for ranked play, where the
/// best of many waiting players matters, and for parties, which must be placed as one.
/// <para>
/// <b>Casual solo play does not need this</b> and keeps the synchronous find-or-create, where an open
/// session is the ticket (MultiplayerPlatform.md §7.1). Tickets exist for what that cannot do: pick
/// the closest-rated opponents from everyone waiting, and seat a group together.
/// </para>
/// <para>
/// A worker forms matches from tickets under a platform-wide lock, creates the session with every
/// matched player already seated, and tells them through the player feed. The seat step's indexes
/// still decide everything — a player seated elsewhere in the meantime cannot be seated twice.
/// </para>
/// </summary>
public class MatchmakingTicket
{
    public Guid Id { get; set; }

    /// <summary>Who queued: the player, or the leader queueing their party.</summary>
    public Guid OwnerUserId { get; set; }

    public Guid? PartyId { get; set; }

    /// <summary>Players this ticket brings — one, or the party.</summary>
    public int Size { get; set; }

    public Guid GameId { get; set; }
    public Guid ModeId { get; set; }
    public Guid? EventId { get; set; }

    /// <summary>Ranked: matched by rating and formed into a rated session. Always solo.</summary>
    public bool IsRanked { get; set; }

    public int ProtocolVersion { get; set; }

    /// <summary>The content language the match is played in, fixed at queue time — the worker has no request to read it from.</summary>
    public Guid LangId { get; set; }

    public Guid? SubjectId { get; set; }
    public Guid? LessonId { get; set; }

    /// <summary>The transport region the queuer asked for, used if their ticket anchors the match.</summary>
    public string? Region { get; set; }

    /// <summary>The ranked rating at queue time. Matching compares these; nothing else reads them.</summary>
    public double RatingMu { get; set; }
    public double RatingSigma { get; set; }

    public TicketState State { get; set; }

    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? MatchedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>The session it was matched into.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>Who must bring the transport room up for that session.</summary>
    public Guid? HostUserId { get; set; }

    /// <summary>Why it ended without a match, or was put back: <c>cancelled</c>, <c>expired</c>, <c>host_no_show</c>, <c>seated_elsewhere</c>.</summary>
    public string? EndReason { get; set; }

    public string? RequestId { get; set; }

    public ICollection<MatchmakingTicketMember> Members { get; set; } = new List<MatchmakingTicketMember>();
    public ICollection<MatchmakingTicketLesson> Lessons { get; set; } = new List<MatchmakingTicketLesson>();
}

/// <summary>
/// A player on a ticket. <see cref="IsLive"/> while the ticket is searching — one live ticket per
/// account, by filtered unique index, the same shape as one live seat.
/// </summary>
public class MatchmakingTicketMember
{
    public Guid TicketId { get; set; }
    public MatchmakingTicket? Ticket { get; set; }

    public Guid UserId { get; set; }

    public bool IsLive { get; set; }
}

/// <summary>
/// For a ticket scoped to a subject: a lesson every one of its players can play. Matching intersects
/// these across tickets, so a match is only ever formed on a lesson all of them can open.
/// </summary>
public class MatchmakingTicketLesson
{
    public Guid TicketId { get; set; }
    public MatchmakingTicket? Ticket { get; set; }

    public Guid LessonId { get; set; }
}

/// <summary><c>Searching → Matched | Cancelled | Expired</c>; a match whose host never came puts the others back to Searching.</summary>
public enum TicketState
{
    Unknown = 0,
    Searching = 1,
    Matched = 2,
    Cancelled = 3,
    Expired = 4
}
