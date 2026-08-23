namespace Share7.Domain.Leaderboards;

/// <summary>
/// Every metric something in this codebase actually raises.
/// <para>
/// Metrics are stored as text so a board is data rather than code, but authoring validates against
/// this list — the same rule <c>RewardEventType</c> documents and for the same reason. A board
/// ranking a metric nothing produces is dead configuration: an operator creates it, sees no error,
/// and waits for a leaderboard that can never fill. **Adding a value here means adding the code
/// that raises it in the same change.**
/// </para>
/// </summary>
public static class LeaderboardMetrics
{
    /// <summary>
    /// Lessons the player has passed, counted once each. Raised when an attempt first takes a
    /// lesson to <c>Completed</c> or better.
    /// </summary>
    public const string LessonsCompleted = "LESSONS_COMPLETED";

    /// <summary>
    /// Lessons answered perfectly, counted once each. Raised when an attempt first takes a lesson
    /// to <c>Aced</c>.
    /// </summary>
    public const string LessonsAced = "LESSONS_ACED";

    /// <summary>
    /// The player's best percentage summed across every lesson they have played — a "how far have
    /// you got, and how well" ladder.
    /// <para>
    /// **Raised as the improvement only**, so a 40% run that later becomes 90% contributes 40 then
    /// 50, and replaying a lesson already at 90% contributes nothing. A metric that counted the
    /// whole score every time would rank whoever replayed the most, not whoever learned the most.
    /// </para>
    /// </summary>
    public const string TotalLessonScore = "TOTAL_LESSON_SCORE";

    /// <summary>
    /// Best score on any single lesson, in whole percent. Raised with each attempt's own
    /// percentage and aggregated with <c>Best</c>, so it tops out at 100 and never falls.
    /// </summary>
    public const string LessonBestPercent = "LESSON_BEST_PERCENT";

    /// <summary>
    /// Every metric a board may be authored against.
    /// <para>
    /// Deliberately short. Distance, survival time and anything else a mini-game measures for
    /// itself needs the authoritative result route before it can appear here — until then there is
    /// nothing to raise it.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        LessonsCompleted,
        LessonsAced,
        TotalLessonScore,
        LessonBestPercent
    };

    public static bool IsKnown(string? metric) => metric is not null && Known.Contains(metric);
}
