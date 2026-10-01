namespace Share7.Domain.Multiplayer;

/// <summary>
/// An account a reserved session holds a place for — today, the players of the match a rematch
/// follows.
/// <para>
/// **The allow-list twin of <see cref="MultiplayerSessionBan"/>**, and enforced in the same place: the
/// seat step's capacity <c>UPDATE</c> refuses anyone without a row here when the session is reserved,
/// so a stranger holding the room's id or code cannot take a seat meant for the previous opponent.
/// A reservation is not a seat: it takes no capacity and nobody is placed in the room until they join.
/// </para>
/// </summary>
public class MultiplayerSessionReservation
{
    public Guid SessionId { get; set; }
    public MultiplayerSession? Session { get; set; }

    public Guid UserId { get; set; }

    public DateTime ReservedAtUtc { get; set; }
}
