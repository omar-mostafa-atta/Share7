namespace Share7.Domain.Multiplayer;

/// <summary>
/// One account's membership of one session.
/// <para>
/// **A membership row is only ever created for the caller in the JWT.** No endpoint accepts a user
/// id in its body, so joining on another child's behalf is not something that is validated against —
/// it is not expressible.
/// </para>
/// </summary>
public class MultiplayerSessionPlayer
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }
    public MultiplayerSession? Session { get; set; }

    /// <summary>From the access token, never from a request body.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 0-based and stable for the life of the membership, so the client can render a deterministic
    /// seat order rather than one that reshuffles whenever the roster is re-read. The host is always
    /// slot 0 at creation; a later host transfer does **not** renumber seats.
    /// </summary>
    public int Slot { get; set; }

    /// <summary>
    /// Mirrors <see cref="MultiplayerSession.HostUserId"/>. Denormalised so the roster read does not
    /// have to join back to the session, and rewritten in the same transaction as the session's own
    /// host field — the two can never be separately stale.
    /// </summary>
    public bool IsHost { get; set; }

    public SessionPlayerStatus Status { get; set; }

    public DateTime JoinedAtUtc { get; set; }

    public DateTime? LeftAtUtc { get; set; }

    /// <summary>
    /// Advanced when the host's heartbeat reports this member on its realtime roster. Also what the
    /// involuntary host claim is measured against: a member may take over once the current host has
    /// not been seen for <c>HostClaimGraceSeconds</c>.
    /// </summary>
    public DateTime LastSeenAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
