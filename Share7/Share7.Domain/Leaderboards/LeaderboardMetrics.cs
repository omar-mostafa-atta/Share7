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
    /// <summary>
    /// How many questions one attempt got right.
    /// <para>
    /// **Raised only by an event entry**, and deliberately not by ordinary play. It is a per-attempt
    /// count rather than a transition, so a board on it would otherwise rank whoever replayed the
    /// same lesson the most — inside an event that is bounded by the event's own entry limits, and
    /// outside one there is nothing to bound it.
    /// </para>
    /// </summary>
    public const string CorrectAnswers = "CORRECT_ANSWERS";

    // ---- multiplayer matches ------------------------------------------------------------------

    /// <summary>
    /// Matches the player took part in and reported a result for, counted once each when the
    /// server decides the match. **A forfeit does not count** — otherwise "play five matches" is
    /// won by joining five and quitting.
    /// </summary>
    public const string MatchesPlayed = "MATCHES_PLAYED";

    /// <summary>
    /// Matches won under the mode's win rule, decided by the server from the players' own results.
    /// <para>
    /// **A walkover is a result, not a win.** Raised only when at least two participants actually
    /// reported — a second account that joins and forfeits must not be a way to farm wins — and
    /// never for a flagged placement.
    /// </para>
    /// </summary>
    public const string MatchesWon = "MATCHES_WON";

    /// <summary>
    /// Tournaments the player took part in, counted once each when the tournament completes — for
    /// everyone still in it at the start, wherever they finished. A player who withdrew before it
    /// began did not take part; one disqualified is not counted.
    /// </summary>
    public const string TournamentsPlayed = "TOURNAMENTS_PLAYED";

    /// <summary>Tournaments won outright: first place, raised once when the tournament completes.</summary>
    public const string TournamentsWon = "TOURNAMENTS_WON";

    /// <summary>
    /// The visible ranked tier reached this season, as a number — 1 bronze, 2 silver, 3 gold,
    /// 4 platinum, 5 diamond. Raised when a player first reaches a tier in a season (placements end,
    /// or a promotion), so an objective aggregated with <c>Best</c> reads "reach gold this month".
    /// <para>
    /// **Only ever rises within a season**, because the tier it reads never drops before the season
    /// ends. Ranked seasons are calendar months in UTC; a <c>Monthly</c> objective lines up with
    /// them exactly when <c>ObjectiveCycle.ResetOffsetHours</c> is 0.
    /// </para>
    /// </summary>
    public const string RankedTier = "RANKED_TIER";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        LessonsCompleted,
        CorrectAnswers,
        MatchesPlayed,
        MatchesWon,
        TournamentsPlayed,
        TournamentsWon,
        RankedTier,
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
