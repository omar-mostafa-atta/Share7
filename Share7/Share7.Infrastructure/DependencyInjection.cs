using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Staff.Interfaces;
using Share7.Infrastructure.Audit;
using Share7.Infrastructure.Staff;
using Share7.Application.Engine.Interfaces;
using Share7.Infrastructure.Engine;
using Share7.Infrastructure.Engine.Reads;
using Share7.Application.Workspace.Interfaces;
using Share7.Infrastructure.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Content.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Assessment.Interfaces;
using Share7.Application.Competency.Interfaces;
using Share7.Application.Organizations.Interfaces;
using Share7.Application.Measurement.Interfaces;
using Share7.Application.Structure.Interfaces;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Equipment.Interfaces;
using Share7.Application.Equipment.Models;
using Share7.Application.Games.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Play.Interfaces;
using Share7.Application.Evidence.Interfaces;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Objectives.Interfaces;
using Share7.Infrastructure.Objectives;
using Share7.Application.Progression.Interfaces;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Runs.Interfaces;
using Share7.Application.Runs.Models;
using Share7.Application.Users.Interfaces;
using Share7.Application.Guidance.Interfaces;
using Share7.Infrastructure.Commerce;
using Share7.Infrastructure.Content;
using Share7.Infrastructure.Assessment;
using Share7.Infrastructure.Competency;
using Share7.Infrastructure.Organizations;
using Share7.Infrastructure.Measurement;
using Share7.Infrastructure.Structure;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Equipment;
using Share7.Infrastructure.Guidance;
using Share7.Infrastructure.Users;
using Share7.Infrastructure.Games;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Progression;
using Share7.Infrastructure.Identity.ExternalAuth;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Admin.Models;
using Share7.Infrastructure.Seeding;
using Share7.Infrastructure.Admin;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Infrastructure.Leaderboards;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Infrastructure.Telemetry;
using Share7.Application.Recovery.Interfaces;
using Share7.Infrastructure.Persistence;
using Share7.Application.Studio.Interfaces;
using Share7.Infrastructure.Recovery;
using Share7.Infrastructure.Studio;
using Share7.Infrastructure.Play;
using Share7.Infrastructure.Evidence;
using Share7.Infrastructure.Progress;
using Share7.Infrastructure.Rewards;
using Share7.Infrastructure.Runs;

namespace Share7.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();

        services.Configure<StudioOptions>(configuration.GetSection(StudioOptions.SectionName));
        var studioOptions = configuration.GetSection(StudioOptions.SectionName).Get<StudioOptions>() ?? new StudioOptions();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ClockSkew = TimeSpan.Zero
                };

                // Admin and SuperAdmin tokens are re-checked against the account on each request
                // (cached for a minute). Student tokens pass through untouched.
                options.Events = new JwtBearerEvents { OnTokenValidated = StaffTokenEvents.ValidatePrivilegedAsync };
            })

            // The Content Studio's own scheme: its own audience and a derived signing key, so a
            // Studio token is refused everywhere but /api/studio, and every other token is refused
            // there. Claims keep their JWT names ("sub", "sid").
            .AddJwtBearer(StaffTokenEvents.StudioScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = StudioTokenIssuer.ValidationParameters(jwtSettings, studioOptions);
                options.Events = new JwtBearerEvents { OnTokenValidated = StaffTokenEvents.ValidateStudioAsync };
            });

        // Content-team accounts, the Studio's sign-in, Team & Access and the audit viewer.
        services.AddMemoryCache();
        services.AddScoped<StudioTokenIssuer>();
        services.AddScoped<StudioSessions>();
        services.AddScoped<StaffScopeReader>();
        services.AddScoped<PeopleDirectory>();
        services.AddScoped<IStudioSessionValidator, StudioSessionValidator>();
        services.AddScoped<IPrivilegedTokenValidator, PrivilegedTokenValidator>();
        services.AddScoped<IStudioAuthService, StudioAuthService>();
        services.AddScoped<IStudioAccountService, StudioAccountService>();
        services.AddScoped<ITeamAdminService, TeamAdminService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        services.AddHttpClient();

        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IExternalLoginValidator, GoogleLoginValidator>();
        services.AddScoped<IExternalLoginValidator, FacebookLoginValidator>();
        services.AddScoped<IGradeService, GradeService>();
        services.AddScoped<ILanguageService, LanguageService>();
        services.AddScoped<ICurriculumService, CurriculumService>();
        services.AddScoped<ICurriculumAdminService, CurriculumAdminService>();
        services.AddScoped<IUserAdminService, UserAdminService>();

        // Read-only counters for the admin console's landing page. Scoped like every other
        // service here because it holds a DbContext.
        services.AddScoped<IAdminOverviewService, AdminOverviewService>();
        services.AddScoped<ILessonQuestionService, LessonQuestionService>();
        services.AddScoped<IQuestionImportService, QuestionImportService>();
        services.AddScoped<ILessonRecoveryQuestionService, LessonRecoveryQuestionService>();
        services.AddScoped<IRecoveryQuestionImportService, RecoveryQuestionImportService>();

        // The paired authoring surface over the same storage the four per-language importers write.
        services.AddScoped<ILessonSheetService, LessonSheetService>();

        // Read-only views over the same tree: how complete it is, and what is in it.
        services.AddScoped<ICurriculumHealthService, CurriculumHealthService>();
        services.AddScoped<ICurriculumSearchService, CurriculumSearchService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IGameAdminService, GameAdminService>();

        // The play-context domain: modes, worlds and events. The resolver is the gate every
        // gameplay entry point runs, which is why it is one registration and not three copies.
        services.AddScoped<IGameModeService, GameModeService>();
        services.AddScoped<IGameModeAdminService, GameModeAdminService>();
        services.AddScoped<IPlaySelectionResolver, PlaySelectionResolver>();
        services.AddScoped<IGameWorldService, GameWorldService>();
        services.AddScoped<IGameWorldAdminService, GameWorldAdminService>();
        services.AddScoped<IPlayEventService, PlayEventService>();
        services.AddScoped<IPlayEventAdminService, PlayEventAdminService>();
        services.AddScoped<IPrizeClaimAdminService, PrizeClaimAdminService>();
        services.AddScoped<IEconomyProfileAdminService, EconomyProfileAdminService>();
        services.AddScoped<IUnlockService, UnlockService>();
        services.AddScoped<IProgressService, ProgressService>();

        // The only writer of educational evidence. Scoped alongside ProgressService because it
        // shares its DbContext and therefore its transaction — evidence that survived a rolled-back
        // attempt would describe gameplay that never happened.
        services.AddScoped<IEvidenceRecorder, EvidenceRecorder>();

        // Scoped, and the lifetime is load-bearing: the minter caches the items it has created
        // within one unit of work, because the two language renderings of one sheet row must
        // resolve to the same item version and a query cannot see rows that are added but unsaved.
        services.AddScoped<IItemIdentityMinter, ItemIdentityMinter>();

        // Rebuilds the node tree from the typed tables. Since the engine rebuild the node tree is the
        // source of truth and the structure service writes both, so this is a backfill and repair
        // tool — the seeder and the contract fixture use it — never the path a live edit takes.
        services.AddScoped<ICurriculumProjector, CurriculumProjector>();

        // ── The education engine (Content Studio plan, Phase 2) ──────────────────────────────
        // One writer of structure, one writer of lesson content; every old authoring path is an
        // adapter over them. Languages are read from the Languages table, not from constants.
        services.AddScoped<IContentLanguages, ContentLanguages>();
        services.AddScoped<ILessonContentReader, LessonContentReader>();
        services.AddScoped<ILessonContentPublisher, LessonContentPublisher>();
        services.AddScoped<ICurriculumStructureService, CurriculumStructureService>();

        // ── The Content Studio's workspace (plan Phase 3) ────────────────────────────────────
        // Drafts, reviews and releases over the engine above. Nothing here writes what students see
        // except a release, which goes through the same two writers as everything else.
        services.AddScoped<IStudioMemberResolver, StudioMemberResolver>();
        services.AddScoped<NodeTrails>();
        services.AddScoped<StudioScope>();
        services.AddScoped<StudioNotifier>();
        services.AddScoped<IRecoveryRuleReader, RecoveryRuleReader>();

        // Phase 5's boards: skills, what children's answers say about the questions, and second
        // chances. None of them publishes anything — the first two change what an answer MEANS and
        // the third is a draft like any other.
        services.AddScoped<IStudioSkillsService, StudioSkillsService>();
        services.AddScoped<IStudioQualityService, StudioQualityService>();
        services.AddScoped<IStudioRecoveryService, StudioRecoveryService>();
        services.AddScoped<IStudioExamsService, StudioExamsService>();
        services.AddScoped<DraftKinds>();
        services.AddScoped<DraftService>();
        services.AddScoped<IDraftService>(sp => sp.GetRequiredService<DraftService>());
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IReleaseService, ReleaseService>();
        services.AddScoped<IStudioCurriculumService, StudioCurriculumService>();
        services.AddScoped<IStudioCurriculaService, StudioCurriculaService>();
        services.AddScoped<IStudioImportService, StudioImportService>();
        services.AddScoped<IStudioInboxService, StudioInboxService>();
        services.AddHostedService<ReleaseScheduler>();

        // No student stranded by a structural change: queued in the change's own transaction,
        // worked off straight after commit, swept again every minute.
        services.AddSingleton<UnlockRepairSignal>();
        services.AddScoped<UnlockRepairRunner>();
        services.AddHostedService<UnlockRepairWorker>();

        // Which tables answer the game's curriculum reads: Curriculum:ReadModel = Legacy | Shadow |
        // Generic, read per request so falling back is one config change and no restart. Shadow
        // serves the typed answer and tallies how often the node answer agreed.
        services.Configure<CurriculumReadOptions>(configuration.GetSection(CurriculumReadOptions.Section));
        services.AddSingleton<CurriculumReadTally>();
        services.AddHostedService<CurriculumReadTallyFlusher>();
        services.AddScoped<TypedCurriculumReads>();
        services.AddScoped<NodeCurriculumReads>();
        services.AddScoped<ICurriculumReads>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<CurriculumReadOptions>>().CurrentValue;

            return options.ReadModel switch
            {
                CurriculumReadModel.Generic => sp.GetRequiredService<NodeCurriculumReads>(),
                CurriculumReadModel.Shadow => new ShadowCurriculumReads(
                    sp.GetRequiredService<TypedCurriculumReads>(),
                    sp.GetRequiredService<NodeCurriculumReads>(),
                    sp.GetRequiredService<CurriculumReadTally>(),
                    options.ShadowSampleRate,
                    sp.GetRequiredService<ILogger<ShadowCurriculumReads>>()),
                _ => sp.GetRequiredService<TypedCurriculumReads>()
            };
        });

        // The measurement layer. Scoped like everything else that shares a unit of work: a
        // projection and the measurements computed from it commit together or not at all.
        services.AddScoped<IObservationProjector, ObservationProjector>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<IContentQualityService, ContentQualityService>();

        // Assessment. The selector is the seam adaptive delivery arrives behind in Phase 5; today
        // it walks a fixed form in order, and registering it by interface is the whole point.
        services.AddScoped<IItemSelector, FixedFormSelector>();
        services.AddScoped<IAssessmentService, AssessmentService>();
        services.AddScoped<IBlueprintAuthoringService, BlueprintAuthoringService>();
        services.AddScoped<IExamCoverageService, ExamCoverageService>();
        services.AddScoped<IExamOutcomeService, ExamOutcomeService>();

        // Authoring real targets over the lesson placeholders. Scoped because a promotion moves
        // mappings and rebuilds observations in one unit of work: a remap that committed without
        // its reprojection would leave every affected learner measured against a claim nothing
        // points at any more.
        services.AddScoped<ITargetAuthoringService, TargetAuthoringService>();

        // Organizations. The access service is the one that matters: every org-side, teacher-side
        // and guardian-side read in the platform resolves through it, because 9.3 is one sentence
        // and one sentence deserves one implementation. A second place deciding who may see a child
        // would be a second answer, and the two would diverge on the first feature that forgot one.
        services.AddScoped<IEducationalAccessService, EducationalAccessService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ICohortService, CohortService>();
        services.AddScoped<IGuardianService, GuardianService>();
        services.AddScoped<IEducationalReportingService, EducationalReportingService>();
        services.AddScoped<ICurriculumOverlayService, CurriculumOverlayService>();
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<ICurrencyAdminService, CurrencyAdminService>();
        services.AddScoped<ILevelService, LevelService>();
        services.AddScoped<IObjectiveProjector, ObjectiveProjector>();
        services.AddScoped<IObjectiveService, ObjectiveService>();
        services.AddScoped<IObjectiveAdminService, ObjectiveAdminService>();
        services.AddScoped<IRewardService, RewardService>();
        services.AddScoped<IRewardAdminService, RewardAdminService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IProductKindAdminService, ProductKindAdminService>();
        services.AddScoped<IProductAdminService, ProductAdminService>();
        services.AddScoped<IProductGrantAdminService, ProductGrantAdminService>();
        services.AddScoped<IOfferService, OfferService>();
        services.AddScoped<IOfferAdminService, OfferAdminService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IGuidanceStateService, GuidanceStateService>();
        services.AddScoped<IGuidanceAdminService, GuidanceAdminService>();
        services.AddScoped<IGuidanceCatalogService, GuidanceCatalogService>();

        services.Configure<RunOptions>(configuration.GetSection(RunOptions.SectionName));
        services.AddScoped<IEarnCeilingService, EarnCeilingService>();

        // The one place counts become currency. Scoped, because it reads and writes counters inside
        // whichever transaction its caller opened — a singleton could not participate in one.
        services.AddScoped<ISignalPricer, SignalPricer>();

        // Retention for the append-only result stream. The service is scoped so one pass is testable;
        // the sweeper is only a timer around it.
        services.AddScoped<IGameResultRetentionService, GameResultRetentionService>();
        services.AddHostedService<GameResultRetentionSweeper>();

        // The level curve, cached for the process rather than for the request. It is at most a few
        // dozen authored rows that change roughly never, and it was being re-read three times per
        // attempt: once for the baseline, once inside the reward engine, once for the response.
        services.AddSingleton<ILevelCurveCache, LevelCurveCache>();
        services.AddScoped<IRunService, RunService>();
        services.AddScoped<IRunAdminService, RunAdminService>();

        // **No IRunLayoutGenerator is registered, and that is the shipped state.** With none, every
        // game settles on the plausibility bounds alone and nothing is ever rejected as impossible.
        // Registering one is a port of the Unity track generator, and a half-ported generator is
        // strictly worse than none — it would reject real runs from real children while appearing to
        // work. The verifier resolves whatever is registered here, so turning verification on for a
        // game is one AddSingleton once its generator is ported and pinned to shared fixture vectors.
        services.AddSingleton<IRunLayoutVerifier, RunLayoutVerifier>();

        services.Configure<EquipmentOptions>(configuration.GetSection(EquipmentOptions.SectionName));
        services.AddScoped<IEquipmentService, EquipmentService>();

        services.Configure<MultiplayerOptions>(configuration.GetSection(MultiplayerOptions.SectionName));
        services.AddScoped<MultiplayerRequestLogStore>();

        // Registered concretely and then forwarded, so matchmaking and the interface resolve to the
        // *same* scoped instance. Matchmaking reuses the session service's seating path rather than
        // owning a second copy of the capacity rules — two implementations is how the direct join
        // and the matchmade join would come to disagree about what "full" means.
        services.AddScoped<MultiplayerSessionService>();
        services.AddScoped<IMultiplayerSessionService>(sp => sp.GetRequiredService<MultiplayerSessionService>());
        services.AddScoped<ISessionLessonMatcher, SessionLessonMatcher>();
        services.AddScoped<IMatchmakingService, MatchmakingService>();
        services.AddScoped<IMultiplayerAdminService, MultiplayerAdminService>();

        services.AddScoped<IMultiplayerSweepService, MultiplayerSweepService>();
        services.AddHostedService<MultiplayerSessionSweeper>();

        // Leaderboards. Note there is no ILeaderboardWriteService and there must never be one:
        // ranking is projected from results the server graded, so the only write seam is
        // IGameResultRecorder, which no controller can reach.
        services.Configure<LeaderboardOptions>(configuration.GetSection(LeaderboardOptions.SectionName));
        services.AddScoped<IDisplayNameService, DisplayNameService>();
        services.AddScoped<IPlausibilityGuard, PlausibilityGuard>();
        services.AddScoped<IGameResultRecorder, GameResultRecorder>();
        services.AddScoped<ILeaderboardProjector, LeaderboardProjector>();
        services.AddScoped<ILeaderboardRolloverService, LeaderboardRolloverService>();
        services.AddScoped<ILeaderboardSettlementService, LeaderboardSettlementService>();

        // Settlement observers. Registered against the interface rather than called from the
        // settlement service directly, so leaderboard machinery never grows a dependency on every
        // feature that cares about a final rank.
        services.AddScoped<ICycleSettlementObserver, EventPrizeAwardService>();
        services.AddScoped<ILeaderboardJobRunner, LeaderboardJobRunner>();
        services.AddScoped<ILeaderboardService, LeaderboardService>();
        services.AddScoped<ILeaderboardAdminService, LeaderboardAdminService>();

        // Telemetry. Ingest is the hottest write path in the platform, so the registry it reads on
        // every batch is a process-lifetime cache rather than a per-request query — the same
        // argument LevelCurveCache makes, only against thousands of reads a second instead of one
        // per attempt. It takes IServiceScopeFactory rather than a DbContext for the reason
        // MultiplayerCompositionTests exists to catch: a singleton holding a scoped context keeps
        // the first request's context alive for the life of the process.
        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));
        services.AddSingleton<ITelemetrySchemaCache, TelemetrySchemaCache>();

        services.AddScoped<ITelemetryIngestService, TelemetryIngestService>();
        services.AddScoped<ITelemetrySchemaService, TelemetrySchemaService>();
        services.AddScoped<ITelemetryAnalyticsService, TelemetryAnalyticsService>();
        services.AddScoped<IUserTimelineService, UserTimelineService>();

        // Scoped services with timers around them, so one pass is testable without standing up a
        // host — the split GameResultRetentionSweeper already established.
        services.AddScoped<ITelemetryRollupService, TelemetryRollupService>();
        services.AddScoped<ITelemetryRetentionService, TelemetryRetentionService>();
        services.AddHostedService<TelemetryProjectorWorker>();
        services.AddHostedService<TelemetryRetentionSweeper>();

        // Content seeding. Registered unconditionally; the options object is what decides whether a
        // call to it does anything, so the startup path and the admin endpoint can both depend on it
        // without either of them knowing the environment.
        services.Configure<ContentSeedOptions>(configuration.GetSection(ContentSeedOptions.SectionName));
        services.AddScoped<IContentSeeder, ContentSeeder>();

        // The audit trail. The actor defaults to the platform itself — right for startup seeding and
        // background jobs — and the API host replaces it with the request's user. TryAdd, so a host
        // that registered its own actor first keeps it.
        services.TryAddScoped<IAuditActor>(_ => SystemAuditActor.Instance);
        services.AddScoped<IAuditLog, AuditLog>();

        // The platform's own accounts, created on startup.
        services.AddScoped<IdentitySeeder>();

        return services;
    }
}
