using Share7.Application.Economy.Models;
using Share7.Application.Progression.Models;
using Share7.Application.Rewards.Models;

namespace Share7.Application.Progress.Models;

/// <summary>Progress for one lesson, for one student, in one game.</summary>
public class LessonProgressDto
{
    public Guid GameId { get; set; }
    public Guid LessonId { get; set; }

    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }

    /// <summary>Rounded to the nearest whole percent.</summary>
    public int Percent { get; set; }

    public int Attempts { get; set; }

    /// <summary>"Uncompleted" | "Completed" | "Aced" — judged on the last attempt.</summary>
    public string CompletionState { get; set; } = nameof(Domain.Progress.CompletionState.Uncompleted);

    public bool IsUnlocked { get; set; }

    /// <summary>True once the student has played it at least once.</summary>
    public bool HasAttempted { get; set; }

    /// <summary>Whether the very first attempt was a clean sweep. Reporting only.</summary>
    public bool FirstAttemptWasPerfect { get; set; }

    /// <summary>The question version this score was earned against.</summary>
    public int QuestionsVersion { get; set; }

    /// <summary>The lesson's current question version in the student's language.</summary>
    public int CurrentQuestionsVersion { get; set; }

    /// <summary>
    /// True when the sheet has been re-uploaded since this score was earned. The score is
    /// deliberately carried forward rather than reset, so this is the signal to prompt
    /// "new questions available, replay this lesson".
    /// </summary>
    public bool ContentUpdated { get; set; }

    public DateTime? LastAttemptAt { get; set; }
}

/// <summary>
/// Aggregate progress for a chapter, subject, term or grade. Computed on read with a
/// <c>GROUP BY</c> over the lesson rows — no table backs this.
/// </summary>
public class NodeProgressDto
{
    public Guid GameId { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public Guid NodeId { get; set; }

    /// <summary>Lessons under this node that are playable in the student's language.</summary>
    public int LessonsTotal { get; set; }
    public int LessonsAttempted { get; set; }

    /// <summary>Lessons currently Completed or Aced.</summary>
    public int LessonsCompleted { get; set; }
    public int LessonsAced { get; set; }

    /// <summary>Summed across the lessons the student has attempted.</summary>
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }

    /// <summary>
    /// Correct answers over the questions in every playable lesson beneath this node, including
    /// lessons never attempted — so an untouched chapter reads 0%, not 100% of nothing.
    /// </summary>
    public int Percent { get; set; }

    public bool IsUnlocked { get; set; }
}

/// <summary>A question the student got wrong (or never answered) on their last run of the lesson.</summary>
public class WrongQuestionDto
{
    public Guid QuestionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid CorrectAnswerId { get; set; }
    public string CorrectAnswerText { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime LastAttemptAt { get; set; }
}

/// <summary>
/// How one question was graded. The server's verdict, not the client's — this is the answer to
/// "was I right", which the client no longer decides.
/// </summary>
public class AnswerResultDto
{
    public Guid QuestionId { get; set; }

    /// <summary>What the student picked, or null if they never answered.</summary>
    public Guid? ChoiceId { get; set; }

    /// <summary>
    /// What was actually right. Already known to the client — the lesson-questions endpoint returns
    /// it so the game can grade offline — so returning it here leaks nothing new and saves a lookup
    /// when rendering the review screen.
    /// </summary>
    public Guid CorrectChoiceId { get; set; }

    public bool IsCorrect { get; set; }
}

/// <summary>A node opened up by an attempt, returned so the client can play the unlock animation.</summary>
public class UnlockedNodeDto
{
    public string NodeType { get; set; } = string.Empty;
    public Guid NodeId { get; set; }
}

/// <summary>Outcome of a submitted attempt: the recomputed score, the new state, and anything it opened.</summary>
public class AttemptResultDto
{
    public Guid GameId { get; set; }
    public Guid LessonId { get; set; }
    public Guid LangId { get; set; }

    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public int Percent { get; set; }
    public int Attempts { get; set; }

    public string CompletionState { get; set; } = string.Empty;
    public bool FirstAttemptWasPerfect { get; set; }
    public int QuestionsVersion { get; set; }

    /// <summary>
    /// The graded result for every question in the lesson, so the client can build a review screen
    /// without regrading anything itself.
    /// <para>
    /// Includes questions the student never answered — those come back with a null
    /// <c>choiceId</c> and <c>isCorrect: false</c>.
    /// </para>
    /// </summary>
    public IReadOnlyList<AnswerResultDto> Answers { get; set; } = [];

    /// <summary>
    /// How many submitted answers named a question that is not part of this lesson in this
    /// language, or a choice that does not belong to its question. They are graded as wrong.
    /// <para>
    /// Non-zero almost always means a **stale cached question set** — the sheet was re-uploaded
    /// since the client downloaded it. Compare <see cref="QuestionsVersion"/> and re-fetch.
    /// </para>
    /// </summary>
    public int UnrecognisedAnswers { get; set; }

    public IReadOnlyList<UnlockedNodeDto> Unlocked { get; set; } = [];

    /// <summary>
    /// What this attempt earned, one entry per reward rule that fired. Empty when nothing matched,
    /// when a rule has already paid, or when a cooldown or daily limit is in force.
    /// <para>
    /// These are **deltas** — what to animate. Do not add them to the local wallet; take
    /// <see cref="Balances"/> instead, which already includes them.
    /// </para>
    /// </summary>
    public IReadOnlyList<RewardDto> Rewards { get; set; } = [];

    /// <summary>
    /// The caller's authoritative balances after the attempt, absolute rather than deltas.
    /// <para>
    /// Returned on every attempt so the wallet reconciles here and needs no follow-up call to
    /// <c>GET /api/commerce/balances</c>. Assign these over the local values — the server's answer
    /// wins.
    /// </para>
    /// </summary>
    public IReadOnlyList<BalanceDto> Balances { get; set; } = [];

    /// <summary>
    /// Where the player stands on the level curve **after** this attempt — absolute, like
    /// <see cref="Balances"/>, and computed server-side.
    /// <para>
    /// Returned on every attempt, not only on the ones that levelled someone up, so the results
    /// screen can fill an XP bar without a second round trip.
    /// </para>
    /// </summary>
    public PlayerLevelDto? Level { get; set; }

    /// <summary>
    /// Levels reached during this attempt, ascending. Empty on almost every attempt.
    /// <para>
    /// A list because one generous grant can cross several, and each is a separate celebration —
    /// and separately a reward rule that may have paid. Whatever those rules paid is already in
    /// <see cref="Rewards"/> and already counted in <see cref="Balances"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<int> LevelsGained { get; set; } = [];
}
