namespace Share7.Domain.Multiplayer;

/// <summary>
/// A seated player asking someone they are allowed to play with (see <c>ISocialPolicy</c>) into their
/// room.
/// <para>
/// **One pending invite per room and recipient**, by filtered unique index: a child pressing
/// "Invite" five times sends one invite, and a second player inviting the same friend to the same
/// room is told the invite already exists rather than stacking another.
/// </para>
/// <para>
/// Accepting seats the recipient through the ordinary seat step, so capacity, bans, reservations and
/// the one-live-seat rule all still decide. An invite is permission to ask for a seat, not a seat.
/// </para>
/// </summary>
public class SessionInvitation
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }
    public MultiplayerSession? Session { get; set; }

    public Guid SenderUserId { get; set; }

    public Guid RecipientUserId { get; set; }

    public InvitationState State { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>A pending invite past this is expired, whatever its row still says.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? AnsweredAtUtc { get; set; }
}

/// <summary>
/// <c>Pending → Accepted | Declined | Cancelled | Expired</c>. Every state but Pending is final.
/// </summary>
public enum InvitationState
{
    Unknown = 0,
    Pending = 1,
    Accepted = 2,
    Declined = 3,

    /// <summary>Withdrawn by the sender, or by a block between the two.</summary>
    Cancelled = 4,

    /// <summary>Its time ran out, or the room it was for ended.</summary>
    Expired = 5
}
