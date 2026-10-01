namespace Share7.Domain.Multiplayer;

/// <summary>
/// An account the host removed from a session, which may not take a seat in it again.
/// <para>
/// **The seat going to <c>Removed</c> is not enough on its own.** Without this row a removed player
/// could rejoin a second later — by id, by the same join code, or through matchmaking finding the same
/// open lobby — and removing an unwanted stranger from a room of children has to actually keep them
/// out. The seat step checks this row inside the same statement that takes capacity, so a rejoin racing
/// the removal cannot slip in between.
/// </para>
/// <para>
/// Per session and never lifted: a ban ends when the session does. It is a host's decision about one
/// room, not a platform sanction — those belong to moderation, which does not exist yet.
/// </para>
/// </summary>
public class MultiplayerSessionBan
{
    public Guid SessionId { get; set; }
    public MultiplayerSession? Session { get; set; }

    /// <summary>The account removed.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The host who removed them. Not a foreign key — the record has to stay legible after they
    /// leave — and kept because "a host who removes everyone" is a pattern worth being able to see.
    /// </summary>
    public Guid BannedByUserId { get; set; }

    public DateTime BannedAtUtc { get; set; }
}
