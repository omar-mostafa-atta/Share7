using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

/// <summary>
/// The curriculum node a match plays over, in the client's own addressing shape.
/// <para>
/// **Opaque to the backend.** Nothing here is a foreign key and nothing validates it — the client
/// resolved this path out of a tree the server served it, so re-checking would only add a way for a
/// legitimate match to be refused. It is stored verbatim, echoed back verbatim, and read by exactly
/// one piece of server logic: matchmaking, which filters on <see cref="LessonId"/>.
/// </para>
/// </summary>
public class CurriculumPathDto
{
    public Guid? GradeId { get; set; }
    public Guid? TermId { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid? ChapterId { get; set; }
    public Guid? LessonId { get; set; }
}

/// <summary>One member of a session, as the roster renders them.</summary>
public class MultiplayerSessionPlayerDto
{
    public Guid UserId { get; set; }

    /// <summary>
    /// The student's own name, from their profile. Null for an account that has not completed one —
    /// the client should fall back to its own placeholder rather than showing an empty seat.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>Stable for the life of the membership, so seat order does not reshuffle on re-read.</summary>
    public int Slot { get; set; }

    public bool IsHost { get; set; }

    public SessionPlayerStatus Status { get; set; }

    public DateTime JoinedAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }
}

/// <summary>
/// A session as every mutating and reading endpoint returns it.
/// <para>
/// One shape for create, get, join, start, close and matchmake on purpose: the client parses a
/// session once, and an endpoint that returned a narrower projection would only invite a second
/// model that drifts from this one.
/// </para>
/// </summary>
public class MultiplayerSessionDto
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public Guid HostUserId { get; set; }

    public string TransportSessionName { get; set; } = string.Empty;

    public string? TransportRegion { get; set; }

    /// <summary>Present only on private sessions.</summary>
    public string? JoinCode { get; set; }

    public MultiplayerSessionState State { get; set; }

    public SessionVisibility Visibility { get; set; }

    public int MaxPlayers { get; set; }

    public int MinPlayers { get; set; }

    public int CurrentPlayerCount { get; set; }

    public int ProtocolVersion { get; set; }

    public bool IsRanked { get; set; }

    public CurriculumPathDto? CurriculumPath { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// The server clock at the moment this response was built, on **every** session response.
    /// <para>
    /// It costs nothing and it removes a round trip: the client can compute its own heartbeat drift
    /// without also calling <c>GET /api/time</c>. Same principle as offer expiry — this machine's
    /// clock decides, never the device's.
    /// </para>
    /// </summary>
    public DateTime ServerTimeUtc { get; set; }

    public List<MultiplayerSessionPlayerDto> Players { get; set; } = [];
}

/// <summary>
/// The answer to a heartbeat. Deliberately narrower than a full session: this is the one response a
/// client receives several times a minute, and it carries only what the host has to act on.
/// </summary>
public class HeartbeatResponse
{
    /// <summary>
    /// **Authoritative.** If the host thinks the match is running and this says <c>Abandoned</c>, the
    /// host is wrong — it lost contact long enough to be swept and must tear down rather than argue.
    /// </summary>
    public MultiplayerSessionState State { get; set; }

    public DateTime ServerTimeUtc { get; set; }

    /// <summary>
    /// How long to wait before the next check-in. Sent every time so the cadence can be changed
    /// server-side — under load, or for a specific build — without shipping a client.
    /// </summary>
    public int NextHeartbeatInSeconds { get; set; }

    /// <summary>The roster as the server now understands it, including anyone just marked missing.</summary>
    public List<MultiplayerSessionPlayerDto> Players { get; set; } = [];
}

/// <summary>What a matchmaking attempt actually did.</summary>
public enum MatchOutcome
{
    Unknown = 0,

    /// <summary>Seated in a session that already existed.</summary>
    Joined,

    /// <summary>Nothing suitable was open, so a new session was created with the caller as host.</summary>
    Created,

    /// <summary>Nothing was open and the caller asked not to create one.</summary>
    NoMatch
}

public class MatchmakeResponse
{
    public MatchOutcome Outcome { get; set; }

    /// <summary>Null only when <see cref="Outcome"/> is <see cref="MatchOutcome.NoMatch"/>.</summary>
    public MultiplayerSessionDto? Session { get; set; }
}
