namespace Share7.Domain.Progress;

/// <summary>
/// How well a lesson has been answered, judged on the <b>last attempt</b> — not a best-ever
/// score. Replaying badly lowers the score and can drop the state back down.
/// <para>
/// <see cref="Aced"/> deliberately means "the last attempt was 100%", not "100% on the first
/// attempt". Under last-attempt semantics the latter would be unrepeatable: one replay would
/// permanently demote a perfect lesson and no lesson could ever be re-aced. The "did it in one
/// go" fact is kept separately on <c>UserLessonProgress.FirstAttemptWasPerfect</c>.
/// </para>
/// </summary>
public enum CompletionState
{
    /// <summary>Never played, or the last attempt scored below the 50% pass mark.</summary>
    Uncompleted = 0,

    /// <summary>Last attempt scored at least 50%. This is what unlocks the next lesson.</summary>
    Completed = 1,

    /// <summary>Last attempt answered every question correctly.</summary>
    Aced = 2
}
