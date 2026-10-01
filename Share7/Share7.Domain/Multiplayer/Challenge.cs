namespace Share7.Domain.Multiplayer;

/// <summary>
/// "Beat my score on this lesson by Friday" — a contest between two players that needs neither of
/// them online at the same time, which is most of the time.
/// <para>
/// **The bar is fixed when the challenge is sent**: the challenger's best score on the lesson in that
/// game, from the server's own grading. The recipient's attempts count once they accept, until the
/// deadline, and are read from the results stream — the same graded attempts every board reads — so
/// nothing a client says about the challenge decides it.
/// </para>
/// <para>
/// One open challenge per challenger, recipient and lesson, by filtered unique index. It pays nothing:
/// two children challenging each other back and forth must not be a way to farm rewards.
/// </para>
/// </summary>
public class Challenge
{
    public Guid Id { get; set; }

    public Guid ChallengerUserId { get; set; }
    public Guid RecipientUserId { get; set; }

    public Guid GameId { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>The challenger's best percent on the lesson when they sent it. Never moves.</summary>
    public int BarPercent { get; set; }

    public ChallengeState State { get; set; }
    public ChallengeOutcome Outcome { get; set; }

    /// <summary>The recipient's best percent inside the window, once they have one.</summary>
    public int? RecipientBestPercent { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime DeadlineUtc { get; set; }

    /// <summary>When it reached a final state, whichever one.</summary>
    public DateTime? EndedAtUtc { get; set; }
}

/// <summary><c>Pending → Accepted → Completed</c>, or <c>Declined | Cancelled | Expired</c>. All but the first two are final.</summary>
public enum ChallengeState
{
    Unknown = 0,
    Pending = 1,
    Accepted = 2,
    Completed = 3,
    Declined = 4,
    Cancelled = 5,

    /// <summary>Not accepted in time, or accepted and never attempted. Nobody won.</summary>
    Expired = 6
}

public enum ChallengeOutcome
{
    None = 0,

    /// <summary>Beaten — decided the moment an attempt above the bar is graded.</summary>
    RecipientWon = 1,

    /// <summary>The bar held until the deadline.</summary>
    ChallengerWon = 2,

    /// <summary>Matched exactly, and never beaten.</summary>
    Draw = 3
}
