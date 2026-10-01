namespace Share7.Domain.Multiplayer;

/// <summary>Where a match's result stands. Stored as text.</summary>
public enum MatchResultState
{
    Unknown = 0,

    /// <summary>Placed by the mode's win rule. Winners, if any, are marked.</summary>
    Decided,

    /// <summary>
    /// The mode has no win rule, so nobody was placed. Who played is still recorded — participation
    /// counts for quests — but nothing was won.
    /// </summary>
    Unranked,

    /// <summary>Voided by an operator. Kept, never deleted: a voided result explains itself.</summary>
    Void
}

/// <summary>
/// The server's verdict on one match: who took part, who placed where, and by which rule.
/// **Append-only.** Written once, when the match can be decided, and never re-decided — a run that
/// settles after the verdict still pays, but it does not move a placement somebody has already seen.
/// <para>
/// <b>Derived, never claimed.</b> No client says who won. Placements are computed from what each
/// player's own settled run and graded answers show, bounded where the server cannot verify, under
/// the rule the mode had when the match was decided. That is what makes a result something a
/// leaderboard, a quest or — eventually — a prize can stand on.
/// </para>
/// <para>
/// Keyed by the session but deliberately **not a foreign key** to it, like <c>Run.SessionId</c>:
/// a result is the durable record of a match, and has to outlive the session row once old sessions
/// are archived.
/// </para>
/// </summary>
public class MatchResult
{
    public Guid SessionId { get; set; }

    public Guid GameId { get; set; }

    public Guid? ModeId { get; set; }

    public Guid? EventId { get; set; }

    /// <summary>The lesson the match played, when it played one. What answer metrics are graded against.</summary>
    public Guid? LessonId { get; set; }

    public MatchResultState State { get; set; }

    /// <summary>
    /// The win rule this result was decided by, **as it stood at the time**. A mode's rule can be
    /// edited; the matches it already decided keep the rule they were played under.
    /// </summary>
    public string? WinRuleJson { get; set; }

    /// <summary>Players who held a seat when the match started, whether or not they reported.</summary>
    public int ParticipantCount { get; set; }

    /// <summary>Participants whose result arrived before the verdict.</summary>
    public int ReportedCount { get; set; }

    /// <summary>
    /// Why the verdict was given when it was: <c>all_reported</c> — every participant's result was
    /// in — or <c>deadline</c> — the match ended and the grace period ran out, so the missing are
    /// forfeits.
    /// </summary>
    public string DecidedBy { get; set; } = string.Empty;

    public DateTime MatchStartedAtUtc { get; set; }

    public DateTime DecidedAtUtc { get; set; }

    public ICollection<MatchPlacement> Placements { get; set; } = new List<MatchPlacement>();
}

/// <summary>One participant's place in one match.</summary>
public class MatchPlacement
{
    public Guid SessionId { get; set; }
    public MatchResult? Result { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Their seat when the match started, so a results screen can list players in seat order.</summary>
    public int Slot { get; set; }

    /// <summary>
    /// 1 is first. **Standard competition ranking** — two players tied for first are both 1st and
    /// the next is 3rd — because telling one of two equal children they came second is not a
    /// tie-break, it is a coin toss. Null when the result is unranked.
    /// </summary>
    public int? Placement { get; set; }

    /// <summary>
    /// Placed first, took part, and ranked clean. Whether a win also *counts* — for a board or a
    /// quest — is a further rule: see <c>MatchResultService</c>.
    /// </summary>
    public bool IsWinner { get; set; }

    /// <summary>Nothing arrived from them before the verdict. Placed after everyone who reported.</summary>
    public bool Forfeited { get; set; }

    /// <summary>
    /// Reported past a bound — more of a signal than the time played allows. Placed after every
    /// clean player, whatever the numbers say. Flagged, never deleted, like every flagged result on
    /// the platform.
    /// </summary>
    public bool Flagged { get; set; }

    /// <summary>Machine token for why, e.g. <c>rate_exceeded:kill</c>.</summary>
    public string? FlagReason { get; set; }

    /// <summary>
    /// The value of every metric the rule ranked on, as JSON (<c>{"correct_answers":8,…}</c>), so a
    /// placement can be explained months later without re-deriving it.
    /// </summary>
    public string ValuesJson { get; set; } = "{}";
}

/// <summary>
/// One graded attempt submitted as part of a match — the server's count of right answers, attributed
/// to the session the player held a seat in.
/// <para>
/// **The only way answers reach a match result.** A run cannot report <c>correct_answer</c> at all
/// (the attempt owns that signal), and an attempt did not know which match it belonged to until it
/// could name the session. Recorded only for a seat holder, only on the lesson the match plays.
/// </para>
/// </summary>
public class MatchAttemptScore
{
    public Guid Id { get; set; }

    /// <summary>Not a foreign key, for the same reason the result is not.</summary>
    public Guid SessionId { get; set; }

    public Guid UserId { get; set; }

    public Guid LessonId { get; set; }

    public int CorrectCount { get; set; }

    public int TotalCount { get; set; }

    public DateTime SubmittedAtUtc { get; set; }
}
