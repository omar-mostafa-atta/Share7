namespace Share7.Domain.Multiplayer;

/// <summary>
/// Where one member stands in a session.
/// <para>
/// <see cref="Disconnected"/> is **not** the same as <see cref="Left"/> and the difference is the
/// entire reconnect story: a child who walks into a lift is Disconnected and keeps their slot for
/// <c>PlayerDisconnectGraceSeconds</c>. Only the sweeper promotes that to Left — the heartbeat never
/// does, because a host that briefly cannot see a peer must not be able to evict them.
/// </para>
/// </summary>
public enum SessionPlayerStatus
{
    Unknown = 0,

    /// <summary>Seated by the backend; the transport has not reported them yet.</summary>
    Joined,

    /// <summary>Present on the host's realtime roster as of the last heartbeat.</summary>
    Connected,

    /// <summary>Missing from the roster, still holding their slot. Reconnect window.</summary>
    Disconnected,

    /// <summary>Left of their own accord. Terminal; the slot is released.</summary>
    Left,

    /// <summary>Removed by the sweeper or an admin. Terminal; the slot is released.</summary>
    Removed
}

/// <summary>Helpers over <see cref="SessionPlayerStatus"/>.</summary>
public static class SessionPlayerStatuses
{
    /// <summary>
    /// Statuses that release the slot and stop counting towards capacity. The two filtered unique
    /// indexes on the membership table are defined against exactly this set, which is what lets a
    /// child rejoin a session they left without colliding with their own old row.
    /// </summary>
    public static readonly IReadOnlySet<SessionPlayerStatus> Departed =
        new HashSet<SessionPlayerStatus>
        {
            SessionPlayerStatus.Left,
            SessionPlayerStatus.Removed
        };

    public static bool HasDeparted(this SessionPlayerStatus status) => Departed.Contains(status);
}
