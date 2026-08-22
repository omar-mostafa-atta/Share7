using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

/// <summary>
/// A session as an operator needs to see it.
/// <para>
/// **Not <see cref="MultiplayerSessionDto"/>, and the differences are the point.** It carries no
/// roster, because a listing of two hundred sessions should not drag two hundred rosters with it —
/// the roster has its own route. And it carries two fields the player-facing shape deliberately
/// omits: <see cref="ClosedReason"/> and <see cref="LastHeartbeatAtUtc"/>, which between them answer
/// "why did this match vanish" without a database session.
/// </para>
/// </summary>
public class MultiplayerSessionSummaryDto
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public Guid HostUserId { get; set; }

    public string TransportSessionName { get; set; } = string.Empty;

    public string? TransportRegion { get; set; }

    public string? JoinCode { get; set; }

    public MultiplayerSessionState State { get; set; }

    public SessionVisibility Visibility { get; set; }

    public int MinPlayers { get; set; }

    public int MaxPlayers { get; set; }

    public int CurrentPlayerCount { get; set; }

    public int ProtocolVersion { get; set; }

    public bool IsRanked { get; set; }

    public Guid? LessonId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// The last check-in. Against <c>serverTimeUtc</c> this is what tells an operator whether a
    /// session is genuinely live or merely has not been swept yet.
    /// </summary>
    public DateTime LastHeartbeatAtUtc { get; set; }

    /// <summary>Why it ended. The first question anyone asks about a session that is not running.</summary>
    public SessionClosedReason? ClosedReason { get; set; }
}

/// <summary>The admin listing's answer, with the clock it was taken against.</summary>
public class MultiplayerAdminSessionsDto
{
    public List<MultiplayerSessionSummaryDto> Sessions { get; set; } = [];

    /// <summary>
    /// How many rows matched before the limit was applied, so an operator can tell a complete answer
    /// from a truncated one rather than guessing from the row count.
    /// </summary>
    public int TotalMatching { get; set; }

    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>Filters for the admin listing. Every field optional, all of them ANDed.</summary>
public class MultiplayerAdminQuery
{
    public Guid? GameId { get; set; }

    public MultiplayerSessionState? State { get; set; }

    /// <summary>Only sessions created before this instant — the "what is still hanging around" query.</summary>
    public DateTime? OlderThanUtc { get; set; }

    /// <summary>
    /// Rows to return. Defaults to 100 and is capped at 500.
    /// <para>
    /// A cap rather than paging because this is a diagnostic surface, not a browsing one: the useful
    /// queries are narrow ones. An uncapped listing over every session ever played is a query nobody
    /// wants the answer to and one that would happily time out at 3 AM.
    /// </para>
    /// </summary>
    public int? Limit { get; set; }
}
