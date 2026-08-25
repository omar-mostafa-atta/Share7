namespace Share7.Domain.Progress;

/// <summary>
/// Rollup of one lesson for one student in one game. Stored rather than derived because the
/// unlock chain and the main game UI read it constantly.
/// <para>
/// Nothing above lesson level is stored. Chapter, subject, term and grade progress are
/// <c>GROUP BY</c> queries over these rows — storing them would mean recomputing every affected
/// student's rows whenever an admin adds a lesson to a chapter, since that changes the
/// denominator.
/// </para>
/// </summary>
public class UserLessonProgress
{
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>Questions answered correctly on the last attempt.</summary>
    public int CorrectCount { get; set; }

    /// <summary>Active questions in the lesson at the time of that attempt.</summary>
    public int TotalCount { get; set; }

    /// <summary>Rounded to the nearest whole percent. Derived from the two counts, stored for cheap reads.</summary>
    public int Percent { get; set; }

    public int Attempts { get; set; }

    /// <summary>
    /// The best <see cref="Percent"/> ever recorded for this lesson, never lowered.
    /// <para>
    /// Kept alongside the last-attempt figures rather than replacing them: the UI shows what the
    /// student just scored, while unlocks, <see cref="CompletionState"/> and any leaderboard metric
    /// read the record. Storing only one of the two forces a choice between "you scored 40%" and
    /// "you have completed this", both of which are true.
    /// </para>
    /// </summary>
    public int BestPercent { get; set; }

    /// <summary>
    /// The most questions ever answered correctly on this lesson. Monotonic, like
    /// <see cref="BestPercent"/> beside it, and the reason replaying a lesson cannot farm XP.
    /// <para>
    /// **Variable XP is paid on improvement, not on attempts.** A rule can be scoped <c>Once</c> per
    /// lesson and is therefore replay-proof for free, but a payout of "five XP a right answer" is not:
    /// without this, a child — or a script — replays one finished lesson a hundred times and is paid
    /// in full every time. Paying the difference instead means acing a lesson pays once, improving
    /// from six to nine pays for three, and a worse replay pays nothing.
    /// </para>
    /// <para>
    /// Deliberately a stored count rather than <c>BestPercent × TotalCount</c>: rounding a percentage
    /// back into a count is off by one often enough to pay for a question nobody answered, and a
    /// lesson's question count changes when its content is revised.
    /// </para>
    /// </summary>
    public int BestCorrectCount { get; set; }

    /// <summary>
    /// **Monotonic** — <c>Uncompleted → Completed → Aced</c>, never backwards. Derived from
    /// <see cref="BestPercent"/>, not from the last attempt.
    /// <para>
    /// A child who aces a lesson and then replays it for fun keeps the ace. The alternative
    /// demotes them for playing more, which is the opposite of what the product wants, and it
    /// makes an <c>Aced</c>-count non-monotonic — something no leaderboard aggregation can model.
    /// </para>
    /// </summary>
    public CompletionState CompletionState { get; set; }

    /// <summary>
    /// Question version this snapshot was scored against. A re-upload deliberately leaves the
    /// snapshot alone — a typo fix in one question should not wipe a student's lesson — so this
    /// going stale against the lesson's current version is what raises <c>contentUpdated</c> on
    /// read.
    /// </summary>
    public int QuestionsVersion { get; set; }

    /// <summary>Set once, on the first attempt, and never recalculated. Reporting only — it does not affect state.</summary>
    public bool FirstAttemptWasPerfect { get; set; }

    public DateTime LastAttemptAt { get; set; }
}
