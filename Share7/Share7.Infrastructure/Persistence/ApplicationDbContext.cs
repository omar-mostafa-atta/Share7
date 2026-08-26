using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Commerce;
using Share7.Domain.Curriculum;
using Share7.Domain.Economy;
using Share7.Domain.Entities;
using Share7.Domain.Equipment;
using Share7.Domain.Games;
using Share7.Domain.Leaderboards;
using Share7.Domain.LookUps;
using Share7.Domain.Multiplayer;
using Share7.Domain.Progress;
using Share7.Domain.Objectives;
using Share7.Domain.Progression;
using Share7.Domain.Rewards;
using Share7.Domain.Runs;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();

    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionChoice> QuestionChoices => Set<QuestionChoice>();
    public DbSet<LessonQuestionUpload> LessonQuestionUploads => Set<LessonQuestionUpload>();

    // The secondary per-lesson pool. Structurally a clone of the four tables above, kept apart so
    // the two pools carry independent versions and one can be re-uploaded without disturbing the
    // other. Trigger logic (when the game shows these) is the client's, not the backend's.
    public DbSet<RecoveryQuestion> RecoveryQuestions => Set<RecoveryQuestion>();
    public DbSet<RecoveryQuestionChoice> RecoveryQuestionChoices => Set<RecoveryQuestionChoice>();
    public DbSet<LessonRecoveryQuestionUpload> LessonRecoveryQuestionUploads => Set<LessonRecoveryQuestionUpload>();

    // The tree carries no text of its own — every node's name lives in its translations.
    public DbSet<GradeTranslation> GradeTranslations => Set<GradeTranslation>();
    public DbSet<TermTranslation> TermTranslations => Set<TermTranslation>();
    public DbSet<SubjectTranslation> SubjectTranslations => Set<SubjectTranslation>();
    public DbSet<ChapterTranslation> ChapterTranslations => Set<ChapterTranslation>();
    public DbSet<LessonTranslation> LessonTranslations => Set<LessonTranslation>();

    /// <summary>Per-language question versions — what <c>Lessons.QuestionsVersion</c> used to be.</summary>
    public DbSet<LessonQuestionSet> LessonQuestionSets => Set<LessonQuestionSet>();

    /// <summary>Per-language recovery-question versions — the same protocol, its own counter.</summary>
    public DbSet<LessonRecoveryQuestionSet> LessonRecoveryQuestionSets => Set<LessonRecoveryQuestionSet>();

    // Avatar outfits. One row per user — the two lists are JSON columns rather than child tables
    // because they are only ever read and written whole.
    public DbSet<UserEquipment> Equipments => Set<UserEquipment>();

    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameTranslation> GameTranslations => Set<GameTranslation>();

    // Progress is per (user, game). Nothing above lesson level is stored — chapter, subject and
    // term progress are GROUP BY queries over UserLessonProgress.
    public DbSet<UserQuestionProgress> UserQuestionProgress => Set<UserQuestionProgress>();
    public DbSet<UserLessonProgress> UserLessonProgress => Set<UserLessonProgress>();
    public DbSet<UserNodeUnlock> UserNodeUnlocks => Set<UserNodeUnlock>();

    // Economy. Virtual currency only — nothing here represents real money. UserCurrencyBalances
    // is the authoritative wallet and the fast projection; CurrencyLedgerEntries is the
    // append-only truth the balances can be rebuilt from.
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<UserCurrencyBalance> UserCurrencyBalances => Set<UserCurrencyBalance>();
    public DbSet<CurrencyLedgerEntry> CurrencyLedgerEntries => Set<CurrencyLedgerEntry>();

    // Rewards. Configuration (what an outcome is worth) is kept apart from the economy (what a
    // balance is) so that a later achievements or events module can pay through the same wallet
    // without either domain knowing about the other.
    public DbSet<RewardRule> RewardRules => Set<RewardRule>();
    public DbSet<RewardRuleGrant> RewardRuleGrants => Set<RewardRuleGrant>();

    /// <summary>Products a rule hands over — badges, mostly. Usually empty.</summary>
    public DbSet<RewardRuleEntitlementGrant> RewardRuleEntitlementGrants => Set<RewardRuleEntitlementGrant>();
    public DbSet<RewardTransaction> RewardTransactions => Set<RewardTransaction>();
    public DbSet<RewardTransactionLine> RewardTransactionLines => Set<RewardTransactionLine>();

    // Commerce. Products are what is sold and Entitlements are who owns them; price and
    // availability belong to Offers, which is why neither of these tables carries one. Ownership
    // outlives the shop — an entitlement stays resolvable after its product is delisted.
    // Shop text is per language, like the curriculum tree: the product and kind rows carry no
    // display name at all, so one product has one id in every language and an entitlement survives
    // a language switch. ProductKind.Name is the exception — it is the client's `kind` token, not
    // a label, so it stays untranslated on the parent row.
    public DbSet<ProductKind> ProductKinds => Set<ProductKind>();
    public DbSet<ProductKindTranslation> ProductKindTranslations => Set<ProductKindTranslation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductTranslation> ProductTranslations => Set<ProductTranslation>();
    public DbSet<ProductGrant> ProductGrants => Set<ProductGrant>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    // Offers are what a product costs; products are what it hands over. One offer can sell several
    // products, so the link is its own table. PurchaseTransactions is append-only and records
    // refusals as well as successes — it is both the audit trail and the idempotency record.
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferTranslation> OfferTranslations => Set<OfferTranslation>();
    public DbSet<OfferProduct> OfferProducts => Set<OfferProduct>();
    public DbSet<PurchaseTransaction> PurchaseTransactions => Set<PurchaseTransaction>();

    // Multiplayer lobbies. Photon Fusion owns the realtime connection; these tables own everything
    // it cannot arbitrate — who is allowed in, how many fit, who is host, and whether the session
    // still exists. Capacity and single-membership are enforced by filtered unique indexes rather
    // than by service-layer checks, so they hold under genuine concurrency.
    public DbSet<MultiplayerSession> MultiplayerSessions => Set<MultiplayerSession>();
    public DbSet<MultiplayerSessionPlayer> MultiplayerSessionPlayers => Set<MultiplayerSessionPlayer>();

    /// <summary>Idempotency keys for multiplayer operations. **Successes only** — see the entity.</summary>
    public DbSet<MultiplayerRequestLog> MultiplayerRequestLogs => Set<MultiplayerRequestLog>();

    /// <summary>Idempotency keys for attempt submissions. **Successes only** — see the entity.</summary>
    public DbSet<ProgressRequestLog> ProgressRequestLogs => Set<ProgressRequestLog>();

    /// <summary>
    /// The level curve. Authored data, read on every attempt — the player's level is derived from
    /// it and their XP balance, and is deliberately not stored anywhere.
    /// </summary>
    public DbSet<LevelThreshold> LevelThresholds => Set<LevelThreshold>();

    // Objectives: quests, achievements and everything shaped like them. One definition table and
    // one counter table for all of it — a daily quest and an achievement differ by how often the
    // counter resets and by nothing else, and modelling them apart is how a platform ends up with
    // several counters that drift.
    public DbSet<Objective> Objectives => Set<Objective>();
    public DbSet<ObjectiveTranslation> ObjectiveTranslations => Set<ObjectiveTranslation>();
    public DbSet<UserObjectiveProgress> UserObjectiveProgress => Set<UserObjectiveProgress>();

    // Missions, weekly challenge sets and season passes: one grouping table with a completion mode,
    // because all three are the same structure with a different rule over the same members.
    public DbSet<ObjectiveGroup> ObjectiveGroups => Set<ObjectiveGroup>();
    public DbSet<ObjectiveGroupTranslation> ObjectiveGroupTranslations => Set<ObjectiveGroupTranslation>();
    public DbSet<UserObjectiveGroupProgress> UserObjectiveGroupProgress => Set<UserObjectiveGroupProgress>();

    /// <summary>
    /// Consecutive-day streaks. The one thing here an objective counter cannot express — no
    /// aggregation distinguishes "seven days running" from "seven days spread over a month".
    /// </summary>
    public DbSet<UserStreak> UserStreaks => Set<UserStreak>();

    /// <summary>
    /// How far each non-leaderboard consumer has read the GameResult stream. Separate from
    /// <c>GameResult.ProjectedAtUtc</c>, which is the leaderboard projector's own single mark.
    /// </summary>
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();

    // Leaderboards. GameResults is the source of truth and everything else in this block is a
    // projection of it: entries, ranks and settlements are all reproducible by replaying that
    // table, which is what makes an index rebuild safe and a lost cache survivable.
    public DbSet<GameResult> GameResults => Set<GameResult>();
    public DbSet<LeaderboardBoard> LeaderboardBoards => Set<LeaderboardBoard>();
    public DbSet<LeaderboardBoardTranslation> LeaderboardBoardTranslations => Set<LeaderboardBoardTranslation>();
    public DbSet<LeaderboardCycle> LeaderboardCycles => Set<LeaderboardCycle>();
    public DbSet<LeaderboardEntry> LeaderboardEntries => Set<LeaderboardEntry>();
    public DbSet<LeaderboardSettlement> LeaderboardSettlements => Set<LeaderboardSettlement>();

    /// <summary>
    /// What a believable result looks like, per game and metric. Authored as data so tightening a
    /// limit after a live exploit is a row edit rather than a release.
    /// </summary>
    public DbSet<LeaderboardMetricBound> LeaderboardMetricBounds => Set<LeaderboardMetricBound>();

    /// <summary>Deferred work, as rows rather than a process. See the entity for why.</summary>
    public DbSet<LeaderboardJob> LeaderboardJobs => Set<LeaderboardJob>();

    /// <summary>
    /// The only name a public board may show. Every other name the schema holds is a child's real
    /// name or their email address.
    /// </summary>
    public DbSet<PlayerDisplayName> PlayerDisplayNames => Set<PlayerDisplayName>();


    // Runs — the pickup economy. A 3D coin is a gameplay signal, not currency: the client reports
    // what it collected, and settlement decides what that was worth. Nothing the client sends carries
    // an amount, and nothing in this domain writes a balance directly — every grant goes through
    // IWalletService, inside the same transaction that records why.
    public DbSet<Run> Runs => Set<Run>();

    /// <summary>
    /// The whole economy-tuning surface. Rebalancing is an UPDATE here, not a client release.
    /// <para>
    /// Read by both surfaces that pay a variable amount — a settled run and a graded attempt — through
    /// the one <c>ISignalPricer</c> that owns the cap ladder.
    /// </para>
    /// </summary>
    public DbSet<SignalValuation> SignalValuations => Set<SignalValuation>();

    /// <summary>Why a run paid what it paid, gross and net. Immutable audit, one row per source.</summary>
    public DbSet<RunPayout> RunPayouts => Set<RunPayout>();

    /// <summary>Gameplay-earned currency per user per UTC day — the counter the earning ceiling reads.</summary>
    public DbSet<DailyCurrencyLedger> DailyCurrencyLedger => Set<DailyCurrencyLedger>();

    /// <summary>
    /// Signals paid for per user per kind per UTC day — the counter <c>SignalValuation.MaxPerDay</c>
    /// is checked against. One keyed row, rather than a group-by over every payout ever written.
    /// </summary>
    public DbSet<DailySignalLedger> DailySignalLedger => Set<DailySignalLedger>();

    // Telemetry — the behaviour record. TelemetryEvents is append-only and the hottest write
    // path here; everything below it is a projection of that stream, in the same relationship
    // LeaderboardEntries has to GameResults.
    //
    // **Nothing in this block is authoritative about anything a child was given.** The ledgers
    // above own every grant, and the user timeline reads them directly rather than a copy kept
    // here — see Docs/AnalyticsArchitecture.md, Rule 2. What telemetry adds is the context those
    // tables have no column for: which screen, how long, what was on offer.
    public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();

    /// <summary>One client play session. The only honest measure of play time the platform has.</summary>
    public DbSet<TelemetrySession> TelemetrySessions => Set<TelemetrySession>();

    /// <summary>
    /// One row per (user, active day), carrying the install cohort and the day index. **The
    /// retention substrate** — D1/D7/D30 is a group-by over this rather than a self-join over raw.
    /// </summary>
    public DbSet<TelemetryUserDay> TelemetryUserDays => Set<TelemetryUserDay>();

    /// <summary>One row per user, kept for the life of the platform after their raw events are swept.</summary>
    public DbSet<TelemetryUserLifecycle> TelemetryUserLifecycle => Set<TelemetryUserLifecycle>();

    /// <summary>Daily counters per event, optionally split by one dimension. One table, not one per chart.</summary>
    public DbSet<TelemetryDailyMetric> TelemetryDailyMetrics => Set<TelemetryDailyMetric>();

    /// <summary>The pre-aggregated retention triangle. Tens of thousands of rows, not billions.</summary>
    public DbSet<TelemetryRetentionCohort> TelemetryRetentionCohorts => Set<TelemetryRetentionCohort>();

    /// <summary>
    /// The event-name registry — category, sampling, retention. What stops ten years of shipping
    /// becoming four thousand event names nobody can tell apart.
    /// </summary>
    public DbSet<TelemetryEventSchema> TelemetryEventSchemas => Set<TelemetryEventSchema>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
