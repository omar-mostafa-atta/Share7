namespace Share7.Domain.Progress;

/// <summary>
/// How well a lesson has been answered, judged on the <b>best attempt ever recorded</b>. The
/// ladder only ever climbs: replaying badly lowers the reported score but never the state.
/// <para>
/// This was last-attempt semantics until leaderboards made it untenable. Under the old rule a
/// child who aced a lesson and replayed it for fun was demoted — and once an <c>Aced</c> count
/// can go down, no ranking aggregation can model it: a leaderboard that subtracts points for
/// playing more is telling children to stop playing.
/// </para>
/// <para>
/// The last attempt is still recorded in full. <c>UserLessonProgress.Percent</c>,
/// <c>CorrectCount</c> and <c>TotalCount</c> are that run; <c>BestPercent</c> is what this state
/// is derived from, and <c>FirstAttemptWasPerfect</c> remains the separate "did it in one go"
/// fact.
/// </para>
/// </summary>
public enum CompletionState
{
    /// <summary>Never played, or no attempt has yet reached the 50% pass mark.</summary>
    Uncompleted = 0,

    /// <summary>Some attempt scored at least 50%. This is what unlocks the next lesson.</summary>
    Completed = 1,

    /// <summary>Some attempt answered every question correctly.</summary>
    Aced = 2
}
