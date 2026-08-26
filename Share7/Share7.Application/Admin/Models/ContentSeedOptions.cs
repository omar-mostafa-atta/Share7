namespace Share7.Application.Admin.Models;

/// <summary>
/// Controls the content seeder. Bound from the <c>ContentSeed</c> configuration section.
/// <para>
/// <b><see cref="Enabled"/> defaults to false and that is deliberate.</b> The seeder writes an
/// operator's content decisions — prices, boards, quests, a whole curriculum — and a deployment
/// that has grown its own content must never have this appear underneath it. Switching it on is an
/// explicit act, made in an environment's own configuration.
/// </para>
/// </summary>
public class ContentSeedOptions
{
    public const string SectionName = "ContentSeed";

    /// <summary>Master switch. Nothing runs when false, including the admin endpoint.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Run the seed during application start. Off means the seeder is reachable only through the
    /// admin API — which is what a deployment usually wants, because a hundred thousand inserts on
    /// the startup path delays the first request behind them.
    /// </summary>
    public bool RunOnStartup { get; set; }

    /// <summary>Platform catalogues: currencies, games, XP ladder, valuations, rewards, shop, quests, boards.</summary>
    public bool Platform { get; set; } = true;

    /// <summary>The curriculum tree and its question sets. This is the slow one — see the count knobs below.</summary>
    public bool Curriculum { get; set; } = true;

    /// <summary>
    /// Demo student accounts with balances, streaks, quest progress and leaderboard entries.
    /// <para>
    /// <b>Off by default even when the seeder is on.</b> These are real Identity accounts with one
    /// shared, known password. That is a liability anywhere the database is reachable from outside
    /// a laptop, so a deployment has to ask for them by name.
    /// </para>
    /// </summary>
    public bool DemoPlayers { get; set; }

    /// <summary>Chapters created under each subject.</summary>
    public int ChaptersPerSubject { get; set; } = 3;

    /// <summary>Lessons created under each chapter.</summary>
    public int LessonsPerChapter { get; set; } = 5;

    /// <summary>
    /// Questions in a lesson's main set, per language. Three choices each, one correct — the
    /// runner has three lanes, so this is the shape the game can actually render.
    /// </summary>
    public int QuestionsPerLesson { get; set; } = 5;

    /// <summary>Questions in a lesson's recovery pool, per language. Zero skips the pool entirely.</summary>
    public int RecoveryQuestionsPerLesson { get; set; } = 3;

    /// <summary>How many demo students to create when <see cref="DemoPlayers"/> is on.</summary>
    public int DemoPlayerCount { get; set; } = 24;

    /// <summary>Password given to every demo account. Only ever read when <see cref="DemoPlayers"/> is on.</summary>
    public string DemoPlayerPassword { get; set; } = "Student123!";

    /// <summary>
    /// Rows pushed per <c>SaveChanges</c>. The curriculum is six figures of rows; one transaction
    /// for the lot is a memory problem and a lock-duration problem at the same time.
    /// </summary>
    public int BatchSize { get; set; } = 4000;
}
