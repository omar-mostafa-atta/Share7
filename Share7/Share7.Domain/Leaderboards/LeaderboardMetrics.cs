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

    // ---- runs (non-curriculum gameplay) ------------------------------------------------------

    /// <summary>
    /// Runs the player has finished and had settled, whatever the outcome. Raised once per settled
    /// run — a run that failed still happened.
    /// </summary>
    public const string RunsSettled = "RUNS_SETTLED";

    /// <summary>Runs whose outcome was <c>Completed</c>. Raised alongside <see cref="RunsSettled"/>.</summary>
    public const string RunsCompleted = "RUNS_COMPLETED";

    /// <summary>
    /// Seconds played, summed. Taken from the run's **server-bounded** duration, never the client's
    /// reported figure, so a modified build cannot inflate a time-played ladder by lying.
    /// </summary>
    public const string RunSeconds = "RUN_SECONDS";

    /// <summary>
    /// The longest single run, in seconds. The same value <see cref="RunSeconds"/> raises, kept as
    /// its own metric because one is a <c>Sum</c> ladder and the other a <c>Best</c> one, and a
    /// board cannot choose an aggregation per entry.
    /// </summary>
    public const string BestRunSeconds = "BEST_RUN_SECONDS";

    /// <summary>
    /// Pickups collected, **as settled** — after the per-run cap, never as reported. Scoped by
    /// pickup kind, so one metric serves coins, gems and every chest a future mini-game invents.
    /// <para>
    /// Raising the reported count would let a claim of 500 coins that settled at 180 still pay a
    /// "collect 500" objective, which is the pickup cap defeated through a side door.
    /// </para>
    /// </summary>
    public const string PickupsCollected = "PICKUPS_COLLECTED";

    /// <summary>
    /// Currency actually credited, scoped by currency key. Net of caps, like everything else here.
    /// One result per settlement rather than per coin — the grant already happens once.
    /// </summary>
    public const string CurrencyEarned = "CURRENCY_EARNED";

    /// <summary>
    /// Every metric a board may be authored against.
    /// <para>
    /// The run metrics arrived with the authoritative result route this list used to be waiting
    /// for. Distance and per-game scores still are not here: the run result carries pickups,
    /// duration and an outcome, and nothing else a mini-game measures for itself. Adding one means
    /// adding a field to the run result and the code that bounds it, in the same change.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        LessonsCompleted,
        LessonsAced,
        TotalLessonScore,
        LessonBestPercent,
        RunsSettled,
        RunsCompleted,
        RunSeconds,
        BestRunSeconds,
        PickupsCollected,
        CurrencyEarned
    };

    public static bool IsKnown(string? metric) => metric is not null && Known.Contains(metric);
}
