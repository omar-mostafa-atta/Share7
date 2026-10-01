namespace Share7.Domain.Multiplayer;

/// <summary>
/// A few players who stay together between matches. The leader picks what to play; "play" opens a
/// private room reserved for exactly the party, and every member is told to come in.
/// <para>
/// **One live party per account**, by filtered unique index on the membership — the same shape as
/// one live seat. Joining another party moves a player out of the old one rather than refusing, so a
/// party someone forgot about can never lock them out of the next.
/// </para>
/// <para>
/// Size is enforced by one conditional <c>UPDATE</c> on <see cref="MemberCount"/>, exactly as session
/// capacity is: two players accepting the last place at once cannot both get it.
/// </para>
/// </summary>
public class Party
{
    public Guid Id { get; set; }

    /// <summary>
    /// Who leads. Not a foreign key: the membership is what cascades from an account, and a party
    /// whose leader has gone is repaired on its next read (the longest-standing member leads).
    /// </summary>
    public Guid LeaderUserId { get; set; }

    public PartyState State { get; set; }

    public int MaxSize { get; set; }

    /// <summary>Members present now. Maintained with the membership, in the same transaction.</summary>
    public int MemberCount { get; set; }

    /// <summary>The room the leader last opened for the party, while it is live.</summary>
    public Guid? CurrentSessionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DisbandedAtUtc { get; set; }

    public ICollection<PartyMember> Members { get; set; } = new List<PartyMember>();
}

public class PartyMember
{
    public Guid PartyId { get; set; }
    public Party? Party { get; set; }

    public Guid UserId { get; set; }

    public DateTime JoinedAtUtc { get; set; }

    /// <summary>Null while a member. A player who leaves and is invited back gets a new row.</summary>
    public DateTime? LeftAtUtc { get; set; }

    public Guid Id { get; set; }
}

public enum PartyState
{
    Unknown = 0,
    Open = 1,
    Disbanded = 2
}

/// <summary>
/// The leader asking a classmate or friend into the party. The same state machine and one-pending
/// rule as <see cref="SessionInvitation"/>.
/// </summary>
public class PartyInvitation
{
    public Guid Id { get; set; }

    public Guid PartyId { get; set; }
    public Party? Party { get; set; }

    public Guid SenderUserId { get; set; }
    public Guid RecipientUserId { get; set; }

    public InvitationState State { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AnsweredAtUtc { get; set; }
}
