namespace Share7.Domain.Multiplayer;

/// <summary>How a tournament pairs its players. Stored as text.</summary>
public enum TournamentFormat
{
    Unknown = 0,

    /// <summary>A bracket: lose once and you are out. Byes go to the top seeds.</summary>
    SingleElimination,

    /// <summary>
    /// Everyone plays every round against someone on a similar record, and nobody is knocked out —
    /// the classroom format: a class of thirty all play five matches, rather than half of them
    /// watching after the first.
    /// </summary>
    Swiss,
    /// <summary>A bounded league in which each pair meets once; fixed seeded schedule.</summary>
    RoundRobin
}

/// <summary>Where a tournament is in its life. Stored as text.</summary>
public enum TournamentState
{
    Unknown = 0,

    /// <summary>Taking entries. Nothing is paired yet.</summary>
    Registration,

    /// <summary>Paired and playing, one round at a time.</summary>
    Running,

    /// <summary>Every round played; placements are final. Terminal.</summary>
    Completed,

    /// <summary>Called off — by its organiser, or because too few entered. Terminal, and pays nothing.</summary>
    Cancelled
}

/// <summary>Where one entrant stands. Stored as text.</summary>
public enum TournamentEntryState
{
    Unknown = 0,

    /// <summary>In the tournament: registered before it starts, still playing after.</summary>
    Entered,

    /// <summary>Lost a knockout match. Keeps the placement the round they went out in earns.</summary>
    Eliminated,

    /// <summary>Left of their own accord, or missed two matches in a row. Not paired again.</summary>
    Withdrawn,

    /// <summary>Removed by an organiser. Never placed and never paid; recorded in the audit log.</summary>
    Disqualified
}

/// <summary>Where one pairing is. Stored as text.</summary>
public enum TournamentMatchState
{
    Unknown = 0,

    /// <summary>Both players known, waiting for one of them to press play before the deadline.</summary>
    Ready,

    /// <summary>A game's room exists — the first to press play opened it, reserved for the pair.</summary>
    Playing,

    /// <summary>Decided, one way or another. See <see cref="TournamentMatch.Outcome"/>.</summary>
    Completed
}

/// <summary>How a pairing was decided — the tokens <see cref="TournamentMatch.Outcome"/> holds.</summary>
public static class TournamentOutcomes
{
    /// <summary>Played, and the match result named one clean winner.</summary>
    public const string Played = "played";

    /// <summary>Played, level, and the format allows a draw (Swiss).</summary>
    public const string Draw = "draw";

    /// <summary>Only one of the pair showed up before the deadline.</summary>
    public const string Walkover = "walkover";

    /// <summary>Nobody to play: the odd player out (Swiss), or an empty slot in the bracket.</summary>
    public const string Bye = "bye";

    /// <summary>
    /// A knockout that could not be settled by play — level after every replay, or both present but
    /// never started — goes to the higher seed. A bracket must produce one winner per match.
    /// </summary>
    public const string Seed = "seed";

    /// <summary>Neither player showed up. Nobody advances.</summary>
    public const string NoShow = "no_show";

    /// <summary>Played, but nobody reported a clean result. Nobody wins (Swiss).</summary>
    public const string NoResult = "no_result";

    /// <summary>
    /// Not played on purpose, because one of the pair has blocked the other and no other pairing was
    /// possible. Scored as a draw in Swiss and by seed in a knockout, and said no more plainly than
    /// that — a block is never revealed to the person blocked.
    /// </summary>
    public const string NotPlayed = "not_played";

    /// <summary>One of the pair withdrew or was disqualified; the other goes through.</summary>
    public const string Forfeit = "forfeit";

    /// <summary>Decided by an organiser, with a reason in the audit log.</summary>
    public const string Organiser = "organiser";

    /// <summary>Both slots empty — a bracket branch nobody reached. Nobody advances.</summary>
    public const string Empty = "empty";
}

/// <summary>
/// A bracket or a Swiss over **reserved sessions**: each pairing is a private room reserved for its
/// two players, opened by whichever presses play first, and decided by the ordinary match result.
/// Ordinary matchmaking never knows tournaments exist (MultiplayerPlatform.md §9.3).
/// <para>
/// <b>Three scopes, one table.</b> An <i>open</i> tournament anybody may enter; an <i>event</i>
/// tournament (<see cref="EventId"/>) takes the event's eligibility rules, window and prize table and
/// pays that table by final placement; a <i>classroom</i> tournament (<see cref="CohortId"/>) is
/// created by the class's teacher, entered only by the class, and pays nothing.
/// </para>
/// <para>
/// <b>Rounds advance on results.</b> A round's pairings are written when the round before it is
/// complete, so every state change is a guarded update on rows that exist — there is no bracket
/// held in memory, and a restarted server picks up exactly where the last one stopped.
/// </para>
/// </summary>
public class Tournament
{
    public Guid Id { get; set; }

    /// <summary>The organiser's title, shown as written. Plain text, never markup.</summary>
    public string Title { get; set; } = string.Empty;

    public Guid GameId { get; set; }

    /// <summary>The mode every match is played in. A versus mode with a win rule — a match nobody can win cannot advance anyone.</summary>
    public Guid ModeId { get; set; }

    /// <summary>The event this is played inside, when it carries prizes: its rules decide who may enter and its prize table pays.</summary>
    public Guid? EventId { get; set; }

    /// <summary>The class this belongs to, for a teacher's tournament. Only the class's learners may enter.</summary>
    public Guid? CohortId { get; set; }

    public TournamentFormat Format { get; set; }

    /// <summary>Rounds a Swiss runs, as asked for; 0 means "as many as it takes to find a winner" (log₂ of the field).</summary>
    public int SwissRounds { get; set; }

    public int MaxEntrants { get; set; }

    /// <summary>
    /// Entrants in, kept beside the entries so the cap is one guarded <c>UPDATE</c> — the same
    /// shape as a session's seat count, and for the same reason: two players taking the last place
    /// at once must not both get it.
    /// </summary>
    public int EntrantCount { get; set; }

    /// <summary>How long each pairing has, from the moment it is ready, before a no-show is called.</summary>
    public int MatchMinutes { get; set; }

    /// <summary>
    /// A subject every match plays a shared lesson from, or null. Each pairing's room narrows to a
    /// lesson both players can play, in the language the first of them is playing in.
    /// </summary>
    public Guid? SubjectId { get; set; }

    /// <summary>The one lesson every match plays, or null. The same questions for everyone.</summary>
    public Guid? LessonId { get; set; }

    public TournamentState State { get; set; }

    /// <summary>When it starts by itself. Null for one its organiser starts by hand — the classroom case.</summary>
    public DateTime? StartsAtUtc { get; set; }

    /// <summary>The round being played. 0 before the start.</summary>
    public int CurrentRound { get; set; }

    /// <summary>How many rounds it will take, fixed at the start from the field that turned up.</summary>
    public int RoundCount { get; set; }

    /// <summary>Breaks equal seeds the same way every time, so a start can be replayed in a test and explained after.</summary>
    public int RandomSeed { get; set; }

    /// <summary>Who created it — a teacher or an operator. An actor, kept as an id like an audit row.</summary>
    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>Why it was called off: <c>too_few_players</c>, or the organiser's own words.</summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// Stamped by every advance. The stamp is not the point — the guarded <c>UPDATE</c> that writes
    /// it is: it holds this row for the advance's transaction, so two servers advancing the same
    /// tournament take turns rather than both writing the next round.
    /// </summary>
    public DateTime? AdvancedAtUtc { get; set; }

    /// <summary>When the event's prize table was paid from the final placements. Null until it has been, so a failed payout is retried.</summary>
    public DateTime? PrizesAwardedAtUtc { get; set; }

    public ICollection<TournamentEntry> Entries { get; set; } = new List<TournamentEntry>();

    public ICollection<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();
}

/// <summary>One player's place in one tournament.</summary>
public class TournamentEntry
{
    public Guid TournamentId { get; set; }
    public Tournament? Tournament { get; set; }

    public Guid UserId { get; set; }

    public TournamentEntryState State { get; set; }

    /// <summary>1 is the strongest. Set at the start, from the mode's hidden rating where one exists.</summary>
    public int Seed { get; set; }

    /// <summary>
    /// Swiss score in half points — 2 for a win or a bye, 1 for a draw — kept whole so that nothing
    /// rounds. Shown as <c>Points / 2</c>.
    /// </summary>
    public int Points { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int Byes { get; set; }

    /// <summary>Pairings missed in a row. Two, and a Swiss entrant is withdrawn rather than handed to opponent after opponent as a free win.</summary>
    public int MissedMatches { get; set; }

    /// <summary>The knockout round they went out in. What their placement is read from.</summary>
    public int? EliminatedInRound { get; set; }

    /// <summary>Final placement, written when the tournament completes. Shared on a tie (two semi-final losers are both 3rd). Null for a disqualified entrant.</summary>
    public int? Placement { get; set; }

    public DateTime RegisteredAtUtc { get; set; }

    /// <summary>When they went out, withdrew, or were disqualified.</summary>
    public DateTime? LeftAtUtc { get; set; }
}

/// <summary>
/// One pairing in one round. Player A is the higher seed. Replayed — a fresh room, the same pairing —
/// when a knockout game ends level, up to <see cref="TournamentBrackets.MaxGamesPerMatch"/> games.
/// </summary>
public class TournamentMatch
{
    public Guid Id { get; set; }

    public Guid TournamentId { get; set; }
    public Tournament? Tournament { get; set; }

    public int Round { get; set; }

    /// <summary>Order within the round. In a knockout, the winners of 2p and 2p+1 meet in the next round's p.</summary>
    public int Position { get; set; }

    /// <summary>
    /// The pair. Not foreign keys: a match is shared history, and when one player's account is erased
    /// their side is blanked rather than the opponent's record deleted with it.
    /// </summary>
    public Guid? PlayerAUserId { get; set; }
    public Guid? PlayerBUserId { get; set; }

    public TournamentMatchState State { get; set; }

    /// <summary>Which game of the pairing this is: 1, then 2 and 3 for knockout replays.</summary>
    public int GameNumber { get; set; } = 1;

    /// <summary>The room for the current game, once one of the pair opened it.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// When each player pressed play for the current game — their check-in. What a no-show is judged
    /// by at the deadline: the player who came wins by walkover.
    /// </summary>
    public DateTime? ACheckedInAtUtc { get; set; }
    public DateTime? BCheckedInAtUtc { get; set; }

    public DateTime? ReadyAtUtc { get; set; }
    public DateTime? DeadlineAtUtc { get; set; }

    /// <summary>Who went through. Null for a draw, a double no-show, or an empty branch.</summary>
    public Guid? WinnerUserId { get; set; }

    /// <summary>How it was decided — one of <see cref="TournamentOutcomes"/>.</summary>
    public string? Outcome { get; set; }

    /// <summary>A placement in the deciding game was flagged. Shown to organisers; never decides on its own.</summary>
    public bool Flagged { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public bool Involves(Guid userId) => PlayerAUserId == userId || PlayerBUserId == userId;

    public Guid? OpponentOf(Guid userId) =>
        PlayerAUserId == userId ? PlayerBUserId : PlayerBUserId == userId ? PlayerAUserId : null;
}
