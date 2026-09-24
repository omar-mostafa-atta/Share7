using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Commerce;
using Share7.Domain.Curriculum;
using Share7.Domain.Economy;
using Share7.Domain.Entities;
using Share7.Domain.Equipment;
using Share7.Domain.Competency;
using Share7.Domain.Content;
using Share7.Domain.Evidence;
using Share7.Domain.Assessment;
using Share7.Domain.Measurement;
using Share7.Domain.Structure;
using Share7.Domain.Games;
using Share7.Domain.Leaderboards;
using Share7.Domain.LookUps;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Domain.Progress;
using Share7.Domain.Objectives;
using Share7.Domain.Organizations;
using Share7.Domain.Progression;
using Share7.Domain.Rewards;
using Share7.Domain.Runs;
using Share7.Domain.Guidance;
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

    /// <summary>
    /// **The main pool only** — a global query filter keeps every reader written before the
    /// recovery pool was merged in seeing exactly the rows it always saw. See
    /// <see cref="ItemLocalizations"/> for every pool.
    /// </summary>
    public DbSet<Question> Questions => Set<Question>();

    /// <summary>
    /// Every per-language rendering of every item, in every pool (<see cref="Question.Role"/>).
    /// The engine's content reads and the publisher go through here; filter by role explicitly.
    /// </summary>
    public IQueryable<Question> ItemLocalizations => Set<Question>().IgnoreQueryFilters();

    /// <summary>
    /// The choices of every rendering in every pool. <c>QuestionChoices</c> has no filter of its
    /// own, but anything that navigates through <c>Choice.Question</c> inherits the main-pool one.
    /// </summary>
    public IQueryable<QuestionChoice> ItemChoices => Set<QuestionChoice>().IgnoreQueryFilters();
    public DbSet<QuestionChoice> QuestionChoices => Set<QuestionChoice>();
    public DbSet<LessonQuestionUpload> LessonQuestionUploads => Set<LessonQuestionUpload>();

    // The secondary per-lesson pool, as it used to be stored. **A compatibility copy since the
    // engine rebuild**: the pool now lives in Questions (Role = Recovery) with the same ids, and the
    // publisher writes both in one transaction until the last reader of these tables has moved.
    // Trigger logic (when the game shows these) is the client's, not the backend's.
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

    /// <summary>Guidance journal state. One row per user, containing the CRDT state snapshot.</summary>
    public DbSet<UserGuidanceState> UserGuidanceStates => Set<UserGuidanceState>();

    public DbSet<Game> Games => Set<Game>();
    public DbSet<GameTranslation> GameTranslations => Set<GameTranslation>();

    // The Mode axis. One row per rule-set a game offers, carrying the half of a mode a shipped
    // client must not be the authority on — whether it is offered, to whom, and what it may pay.
    public DbSet<GameMode> GameModes => Set<GameMode>();
    public DbSet<GameModeTranslation> GameModeTranslations => Set<GameModeTranslation>();

    /// <summary>How much of what a session earns is actually paid. Referenced by modes and events.</summary>
    public DbSet<EconomyProfile> EconomyProfiles => Set<EconomyProfile>();

    // The World axis. Policy rows only — the art is client content, and this server never resolves it.
    public DbSet<GameWorld> GameWorlds => Set<GameWorld>();
    public DbSet<GameWorldTranslation> GameWorldTranslations => Set<GameWorldTranslation>();

    // Events, and what winning one is worth. The window lives on the bound leaderboard cycle, never here.
    public DbSet<PlayEvent> PlayEvents => Set<PlayEvent>();
    public DbSet<PlayEventTranslation> PlayEventTranslations => Set<PlayEventTranslation>();
    public DbSet<EventPrizeTier> EventPrizeTiers => Set<EventPrizeTier>();
    public DbSet<EventPrizeTierTranslation> EventPrizeTierTranslations => Set<EventPrizeTierTranslation>();

    /// <summary>What a placing won. Append-once per (event, cohort, user); never rewritten by a rebuild.</summary>
    public DbSet<EventAward> EventAwards => Set<EventAward>();

    /// <summary>Fulfilment of a real-world prize, carrying no personal data by design.</summary>
    public DbSet<PrizeClaim> PrizeClaims => Set<PrizeClaim>();

    // Progress is per (user, game). Nothing above lesson level is stored — chapter, subject and
    // term progress are GROUP BY queries over UserLessonProgress.
    public DbSet<UserQuestionProgress> UserQuestionProgress => Set<UserQuestionProgress>();
    public DbSet<UserLessonProgress> UserLessonProgress => Set<UserLessonProgress>();
    public DbSet<UserNodeUnlock> UserNodeUnlocks => Set<UserNodeUnlock>();

    // Educational evidence. LearnerResponses is the append-only truth every educational capability
    // is derived from — the counterpart to CurrencyLedgerEntries, which the education domain went
    // without until Phase 0 of the rebuild. The progress tables above are projections of it, kept
    // because stars and unlocks are legitimately per-game; knowledge is not.
    //
    // EvidenceContracts are the only bridge from gameplay to education: LearnerResponse names a
    // published contract version, non-nullably, so no interaction becomes evidence without one.
    public DbSet<EvidenceContract> EvidenceContracts => Set<EvidenceContract>();
    public DbSet<EvidenceContractVersion> EvidenceContractVersions => Set<EvidenceContractVersion>();
    public DbSet<LearnerResponse> LearnerResponses => Set<LearnerResponse>();

    // Item identity. An Item is a question as a thing that exists, across rewrites and languages;
    // an ItemVersion is one frozen revision; today's Question row is a per-language rendering of
    // one. Evidence names all three, and only the rendering is allowed to disappear.
    public DbSet<ItemBank> ItemBanks => Set<ItemBank>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemVersion> ItemVersions => Set<ItemVersion>();

    // What is served, per node, pool and language, and every version that was ever published. The
    // game's version protocol reads PublishedItemSets; LessonQuestionSets and
    // LessonRecoveryQuestionSets are written beside them as compatibility copies until cutover.
    public DbSet<PublishedItemSet> PublishedItemSets => Set<PublishedItemSet>();
    public DbSet<ContentPublication> ContentPublications => Set<ContentPublication>();

    // Competency. LearningTarget is the only thing proficiency can be about; everything in the
    // measurement layer is keyed by one.
    public DbSet<CompetencyFramework> CompetencyFrameworks => Set<CompetencyFramework>();
    public DbSet<LearningTarget> LearningTargets => Set<LearningTarget>();
    public DbSet<LearningTargetTranslation> LearningTargetTranslations => Set<LearningTargetTranslation>();
    public DbSet<LearningTargetEdge> LearningTargetEdges => Set<LearningTargetEdge>();
    public DbSet<LearningTargetAlignment> LearningTargetAlignments => Set<LearningTargetAlignment>();
    public DbSet<ItemTargetMapping> ItemTargetMappings => Set<ItemTargetMapping>();
    public DbSet<NodeTargetMapping> NodeTargetMappings => Set<NodeTargetMapping>();

    // Curriculum structure as data. CurriculumNodes is the source of truth, keeping the exact ids
    // of the legacy typed tree, which is written beside it as a compatibility copy until cutover.
    public DbSet<CurriculumAuthority> CurriculumAuthorities => Set<CurriculumAuthority>();
    public DbSet<Domain.Structure.Curriculum> Curricula => Set<Domain.Structure.Curriculum>();
    public DbSet<CurriculumVersion> CurriculumVersions => Set<CurriculumVersion>();
    public DbSet<CurriculumNodeKind> CurriculumNodeKinds => Set<CurriculumNodeKind>();
    public DbSet<CurriculumNode> CurriculumNodes => Set<CurriculumNode>();
    public DbSet<CurriculumNodeTranslation> CurriculumNodeTranslations => Set<CurriculumNodeTranslation>();
    public DbSet<NodeItemMapping> NodeItemMappings => Set<NodeItemMapping>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    // The shadow-read tally: per day and per game read, how often the node path agreed with the
    // typed one. The evidence for switching Curriculum:ReadModel to Generic.
    public DbSet<CurriculumReadCheck> CurriculumReadChecks => Set<CurriculumReadCheck>();

    // Structural changes queue these in their own transaction so no student is stranded by them.
    public DbSet<UnlockRepairJob> UnlockRepairJobs => Set<UnlockRepairJob>();

    // The Content Studio's workspace (plan Phase 3): drafts nobody but the team sees, their reviews
    // and comments, and the releases that are the only way any of it reaches students.
    public DbSet<Share7.Domain.Workspace.Draft> Drafts => Set<Share7.Domain.Workspace.Draft>();
    public DbSet<Share7.Domain.Workspace.DraftContributor> DraftContributors => Set<Share7.Domain.Workspace.DraftContributor>();
    public DbSet<Share7.Domain.Workspace.DraftComment> DraftComments => Set<Share7.Domain.Workspace.DraftComment>();
    public DbSet<Share7.Domain.Workspace.ReviewDecision> ReviewDecisions => Set<Share7.Domain.Workspace.ReviewDecision>();
    public DbSet<Share7.Domain.Workspace.DraftPresence> DraftPresence => Set<Share7.Domain.Workspace.DraftPresence>();
    public DbSet<Share7.Domain.Workspace.Release> Releases => Set<Share7.Domain.Workspace.Release>();
    public DbSet<Share7.Domain.Workspace.ReleaseEntry> ReleaseEntries => Set<Share7.Domain.Workspace.ReleaseEntry>();
    public DbSet<Share7.Domain.Workspace.WorkAssignment> StudioAssignments => Set<Share7.Domain.Workspace.WorkAssignment>();
    public DbSet<Share7.Domain.Workspace.StudioNotification> StudioNotifications => Set<Share7.Domain.Workspace.StudioNotification>();

    // When a child is offered second-chance questions, and how many (plan Phase 5). Written at a
    // node, released like a lesson's questions, and read by the game only when the game asks — so
    // a rule can be proposed, reviewed and released without changing a single running client.
    public DbSet<Share7.Domain.Recovery.RecoveryRule> RecoveryRules => Set<Share7.Domain.Recovery.RecoveryRule>();

    // Organizations. Five tables and one scoping column, which is the whole of multi-tenancy here:
    // an org sees a learner's evidence through enrolments it owns (Enrollment.OwnerOrgId), so a
    // school that adopts Share7 gets the work done under its own enrolment and acquires nothing
    // retroactively from the learner's private one. Docs/EducationalArchitecture.md 9.1-9.4.
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Cohort> Cohorts => Set<Cohort>();
    public DbSet<CohortMembership> CohortMemberships => Set<CohortMembership>();
    public DbSet<GuardianLink> GuardianLinks => Set<GuardianLink>();
    public DbSet<Assignment> Assignments => Set<Assignment>();

    // A school's edits to a curriculum it does not own, held as a diff and resolved at read time.
    // Never a mutation of the official version - that is one of the four independent mechanisms
    // that keep an official curriculum uncorruptible (17.2).
    public DbSet<CurriculumOverlay> CurriculumOverlays => Set<CurriculumOverlay>();
    public DbSet<CurriculumOverlayEdit> CurriculumOverlayEdits => Set<CurriculumOverlayEdit>();

    // Measurement. Everything here is derived from LearnerResponses and rebuildable by replaying
    // them — except ItemStatistics, which is a running aggregate on purpose so that erasing one
    // learner cannot move another learner's numbers.
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<ItemStatistics> ItemStatistics => Set<ItemStatistics>();
    public DbSet<Domain.Measurement.Measurement> Measurements => Set<Domain.Measurement.Measurement>();
    public DbSet<MasteryRule> MasteryRules => Set<MasteryRule>();
    public DbSet<MasteryVerdict> MasteryVerdicts => Set<MasteryVerdict>();

    // Assessment. A blueprint says what a test is supposed to measure; a form is one immutable
    // realisation of it; an administration is one learner sitting one form under one set of
    // conditions. The blueprint is also the object an exam board publishes, which is what lets
    // coverage be computed against a real examination the platform does not administer.
    public DbSet<AssessmentBlueprint> AssessmentBlueprints => Set<AssessmentBlueprint>();
    public DbSet<AssessmentBlueprintArea> AssessmentBlueprintAreas => Set<AssessmentBlueprintArea>();
    public DbSet<AssessmentBlueprintLine> AssessmentBlueprintLines => Set<AssessmentBlueprintLine>();
    public DbSet<Domain.Assessment.Assessment> Assessments => Set<Domain.Assessment.Assessment>();
    public DbSet<AssessmentForm> AssessmentForms => Set<AssessmentForm>();
    public DbSet<AssessmentFormItem> AssessmentFormItems => Set<AssessmentFormItem>();
    public DbSet<AssessmentAdministration> AssessmentAdministrations => Set<AssessmentAdministration>();

    // Examinations Share7 does not administer, and what the platform is willing to say about them.
    // ExamProjections are derived and disposable; ReportedExamOutcomes are not — they are the one
    // input to calibration that cannot be reasoned into existence and must be collected.
    public DbSet<ExamSpecification> ExamSpecifications => Set<ExamSpecification>();
    public DbSet<ExamSpecificationVersion> ExamSpecificationVersions => Set<ExamSpecificationVersion>();
    public DbSet<ReportedExamOutcome> ReportedExamOutcomes => Set<ReportedExamOutcome>();
    public DbSet<ExamProjection> ExamProjections => Set<ExamProjection>();
    public DbSet<ExamProjectionGap> ExamProjectionGaps => Set<ExamProjectionGap>();

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

    // Guidance Framework & Remotely Manageable CMS
    public DbSet<GuidanceFlow> GuidanceFlows => Set<GuidanceFlow>();
    public DbSet<GuidanceFlowVersion> GuidanceFlowVersions => Set<GuidanceFlowVersion>();
    public DbSet<GuidanceAuditLog> GuidanceAuditLogs => Set<GuidanceAuditLog>();

    /// <summary>
    /// The platform-wide audit trail: who did what to curriculum, questions and accounts. Append-only,
    /// enforced by a trigger — see <see cref="Share7.Domain.Audit.AuditEvent"/>.
    /// </summary>
    public DbSet<Share7.Domain.Audit.AuditEvent> AuditEvents => Set<Share7.Domain.Audit.AuditEvent>();

    // Content-team accounts and Studio sign-in — Team & Access. See Share7.Domain.Staff.
    public DbSet<Share7.Domain.Staff.StaffProfile> StaffProfiles => Set<Share7.Domain.Staff.StaffProfile>();
    public DbSet<Share7.Domain.Staff.StaffScopeNode> StaffScopeNodes => Set<Share7.Domain.Staff.StaffScopeNode>();
    public DbSet<Share7.Domain.Staff.StaffScopeLanguage> StaffScopeLanguages => Set<Share7.Domain.Staff.StaffScopeLanguage>();
    public DbSet<Share7.Domain.Staff.StaffSetupToken> StaffSetupTokens => Set<Share7.Domain.Staff.StaffSetupToken>();
    public DbSet<Share7.Domain.Staff.StaffSession> StaffSessions => Set<Share7.Domain.Staff.StaffSession>();
    public DbSet<Share7.Domain.Staff.StaffSignInEvent> StaffSignInEvents => Set<Share7.Domain.Staff.StaffSignInEvent>();
    public DbSet<Share7.Domain.Staff.StaffSecuritySettings> StaffSecuritySettings => Set<Share7.Domain.Staff.StaffSecuritySettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
