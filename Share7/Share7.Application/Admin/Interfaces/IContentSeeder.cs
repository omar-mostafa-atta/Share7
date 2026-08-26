namespace Share7.Application.Admin.Interfaces;

/// <summary>
/// Fills an empty database with a complete, playable world: the platform catalogues the client
/// reads on launch, a full curriculum tree with real questions, and — where an environment asks
/// for them — demo players so the social surfaces have something to show.
/// <para>
/// <b>Why a service rather than a migration.</b> Migrations describe schema and the handful of rows
/// the <i>code</i> depends on — <c>Grades</c>, <c>Languages</c>, the <c>xp</c> currency. What this
/// writes is <i>content</i>: an operator's decisions about prices, boards, quests and lessons.
/// Freezing content into a migration makes it undeletable history that every environment replays
/// forever. A gated service runs where it is switched on and nowhere else, and can be re-run after
/// a content change without a schema version.
/// </para>
/// <para>
/// <b>It ships to deployment on purpose.</b> A fresh production database has the same empty
/// catalogues a laptop does — no games, no boards, no shop, no lessons — and there is no admin
/// screen that can author fifteen hundred lessons by hand. So this is deployable, gated by
/// <c>ContentSeed:Enabled</c> and additionally reachable through the admin API, and its default is
/// off so that turning it on stays a deliberate act.
/// </para>
/// <para>
/// <b>Everything it writes is idempotent and additive.</b> Rows are matched on their natural key —
/// a currency by <c>Key</c>, a term by (grade, order), an objective by <c>Key</c> — and an existing
/// row is left exactly as it is. Re-running never duplicates and never overwrites a value somebody
/// has since retuned, which is the property that makes running it against a live database safe.
/// </para>
/// </summary>
public interface IContentSeeder
{
    /// <summary>
    /// Seeds everything enabled by configuration and reports what was written.
    /// <para>
    /// Returns an empty report — not an error — when <c>ContentSeed:Enabled</c> is false, so the
    /// startup path can call it unconditionally.
    /// </para>
    /// </summary>
    Task<ContentSeedReport> SeedAsync(CancellationToken cancellationToken);
}

/// <summary>What one seeding run created, per area. Zero everywhere means the database was already full.</summary>
public sealed class ContentSeedReport
{
    public bool Skipped { get; set; }

    public int Currencies { get; set; }
    public int Games { get; set; }
    public int LevelThresholds { get; set; }
    public int SignalValuations { get; set; }
    public int MetricBounds { get; set; }
    public int RewardRules { get; set; }
    public int Products { get; set; }
    public int Offers { get; set; }
    public int Objectives { get; set; }
    public int ObjectiveGroups { get; set; }
    public int LeaderboardBoards { get; set; }
    public int LeaderboardCycles { get; set; }

    public int Terms { get; set; }
    public int Subjects { get; set; }
    public int Chapters { get; set; }
    public int Lessons { get; set; }
    public int Questions { get; set; }
    public int RecoveryQuestions { get; set; }

    public int DemoPlayers { get; set; }
    public int LeaderboardEntries { get; set; }

    public TimeSpan Elapsed { get; set; }

    public bool WroteAnything =>
        Currencies + Games + LevelThresholds + SignalValuations + MetricBounds + RewardRules
        + Products + Offers + Objectives + ObjectiveGroups + LeaderboardBoards + LeaderboardCycles
        + Terms + Subjects + Chapters + Lessons + Questions + RecoveryQuestions
        + DemoPlayers + LeaderboardEntries > 0;

    public override string ToString() =>
        Skipped
            ? "content seed skipped (ContentSeed:Enabled is false)"
            : $"currencies={Currencies} games={Games} levels={LevelThresholds} valuations={SignalValuations} "
              + $"metricBounds={MetricBounds} rewardRules={RewardRules} products={Products} offers={Offers} "
              + $"objectives={Objectives} objectiveGroups={ObjectiveGroups} boards={LeaderboardBoards} "
              + $"cycles={LeaderboardCycles} terms={Terms} subjects={Subjects} chapters={Chapters} "
              + $"lessons={Lessons} questions={Questions} recoveryQuestions={RecoveryQuestions} "
              + $"demoPlayers={DemoPlayers} leaderboardEntries={LeaderboardEntries} "
              + $"in {Elapsed.TotalSeconds:F1}s";
}
