namespace Share7.Domain.Progress;

/// <summary>
/// One row per (student, game, question) — the source of truth the lesson rollup and the
/// "which questions keep going wrong" report are both derived from.
/// <para>
/// Rows are rewritten on every attempt: <see cref="IsCorrect"/> reflects the <b>last</b>
/// attempt, matching the lesson-level semantics. A question the student never answered is
/// recorded as incorrect, which is sound because one run shows every question in the lesson.
/// </para>
/// </summary>
public class UserQuestionProgress
{
    public Guid UserId { get; set; }

    /// <summary>Progress is tracked independently per game — acing a lesson in one proves nothing about another.</summary>
    public Guid GameId { get; set; }

    public Guid QuestionId { get; set; }

    /// <summary>
    /// Denormalized from the question. Every read filters by lesson, and carrying it here saves
    /// a join on the hot paths; a question never moves between lessons, so it cannot drift.
    /// </summary>
    public Guid LessonId { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>How many attempts have included this question.</summary>
    public int Attempts { get; set; }

    public DateTime LastAttemptAt { get; set; }
}
