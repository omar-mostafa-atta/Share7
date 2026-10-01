namespace Share7.Domain.Social;

/// <summary>
/// One player has blocked another. **Either direction blocks both**: matchmaking never seats the
/// pair together, and neither can invite, challenge or see the other's presence.
/// <para>
/// The blocked player is never told. Every refusal a block causes reads exactly like "you are not
/// connected to this player", so blocking someone cannot start an argument or be probed for.
/// </para>
/// </summary>
public class PlayerBlock
{
    /// <summary>Who blocked.</summary>
    public Guid UserId { get; set; }

    /// <summary>Who is blocked.</summary>
    public Guid BlockedUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// When a player's client last showed signs of life — its event-feed poll. The one input to
/// "online"; "in a lobby" and "in a match" come from seats, which are already authoritative.
/// <para>
/// Written at most once every <c>PresenceWriteSeconds</c> per player, so a room full of clients
/// long-polling is not a room full of writes.
/// </para>
/// </summary>
public class PlayerPresence
{
    public Guid UserId { get; set; }

    public DateTime LastSeenAtUtc { get; set; }
}
