namespace Share7.Domain.Social;

/// <summary>
/// A friendship, stored as **two rows** — one from each side — so "my friends" is one seek on
/// <see cref="UserId"/> and removing it from either side removes both.
/// <para>
/// Only ever created by mutual agreement over a friend code, and only between players allowed to
/// have friends at all: an adult, or a child whose guardian granted <c>SocialPlay</c>. If that
/// consent is later withdrawn the rows stay but stop connecting anyone — see <c>FriendGraph</c>.
/// </para>
/// </summary>
public class Friendship
{
    public Guid UserId { get; set; }
    public Guid FriendUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A player's friend code: eight characters they can read out or show to someone they know. Minted
/// only for a player allowed to have friends; rotating it makes the old one open nothing.
/// </summary>
public class PlayerFriendCode
{
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// "Be my friend", sent by entering the other player's code. Becomes a friendship only when they
/// accept — a code that leaked to a stranger still cannot make them anyone's friend.
/// </summary>
public class FriendRequest
{
    public Guid Id { get; set; }
    public Guid SenderUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    public FriendRequestState State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AnsweredAtUtc { get; set; }
}

public enum FriendRequestState
{
    Unknown = 0,
    Pending = 1,
    Accepted = 2,
    Declined = 3,

    /// <summary>Withdrawn by a block between the two.</summary>
    Cancelled = 4
}
