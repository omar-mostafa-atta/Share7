using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Telemetry;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Telemetry;

/// <summary>
/// The event registry: seeding it, and letting an operator re-author a row.
/// <para>
/// Seeded at startup so a fresh database has a vocabulary before its first client connects — the
/// alternative is every launch event landing as "unregistered" and nothing rolling up until
/// somebody notices.
/// </para>
/// </summary>
public class TelemetrySchemaService : ITelemetrySchemaService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITelemetrySchemaCache _cache;

    public TelemetrySchemaService(ApplicationDbContext dbContext, ITelemetrySchemaCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<ServiceResult<EventCatalogueRowDto>> UpsertAsync(
        string name, UpsertEventSchemaRequest request, CancellationToken cancellationToken)
    {
        if (!TelemetryPrivacy.IsValidEventName(name))
            return ServiceResult<EventCatalogueRowDto>.Invalid("Event names must be snake_case.");

        if (request.SampleRate is <= 0 or > 1)
            return ServiceResult<EventCatalogueRowDto>.Invalid("Sample rate must be above 0 and at most 1.");

        if (request.Category == TelemetryCategory.Unknown)
        {
            // Refused rather than defaulted. The category is the lawful basis this event is
            // collected under, and guessing at one on an operator's behalf is the single decision
            // in this system nobody should be able to make by omission.
            return ServiceResult<EventCatalogueRowDto>.Invalid(
                "An event must declare whether it is Operational or Behavioural.");
        }

        if (request.RetentionDays is <= 0)
            return ServiceResult<EventCatalogueRowDto>.Invalid("Retention must be at least one day, or null.");

        var dimensions = NormaliseDimensions(request.Dimensions, out var unknown);

        if (unknown is not null)
        {
            return ServiceResult<EventCatalogueRowDto>.Invalid(
                $"Unknown dimension '{unknown}'. Valid: {string.Join(", ", TelemetryDimensions.All)}.");
        }

        var now = DateTime.UtcNow;

        var schema = await _dbContext.TelemetryEventSchemas
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);

        if (schema is null)
        {
            schema = new TelemetryEventSchema { Name = name, CreatedAtUtc = now };
            _dbContext.TelemetryEventSchemas.Add(schema);
        }

        schema.Group = Trim(request.Group, 32, "general");
        schema.Description = Trim(request.Description, 256, string.Empty);
        schema.Category = request.Category;
        schema.SampleRate = request.SampleRate;
        schema.RetentionDays = request.RetentionDays;
        schema.Enabled = request.Enabled;
        schema.RollUpDaily = request.RollUpDaily;
        schema.Dimensions = dimensions;
        schema.UpdatedAtUtc = now;

        // Clearing FirstSeenAtUtc is what "registering" an unrecognised name *means*: it leaves the
        // review queue and its events start folding into rollups from the next projector pass.
        // Past events stay unfolded — backfilling them would mean rewinding the watermark for
        // everybody, and a metric that starts on the day it was registered is honest about itself.
        schema.FirstSeenAtUtc = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _cache.Invalidate();

        return ServiceResult<EventCatalogueRowDto>.Success(new EventCatalogueRowDto
        {
            Name = schema.Name,
            Group = schema.Group,
            Description = schema.Description,
            Category = schema.Category,
            SampleRate = schema.SampleRate,
            RetentionDays = schema.RetentionDays,
            Enabled = schema.Enabled,
            RollUpDaily = schema.RollUpDaily,
            Dimensions = schema.Dimensions,
            FirstSeenAtUtc = null
        });
    }

    /// <summary>
    /// Registers every name in the shipped vocabulary that has no row yet.
    /// <para>
    /// **Adds, never overwrites.** An operator who turned an event's sampling down to 5% last month
    /// would otherwise find it back at 100% after every deploy — which is the kind of silent
    /// regression that only shows up as a bill.
    /// </para>
    /// </summary>
    public async Task<int> SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.TelemetryEventSchemas
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

        var known = existing.ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var seed in TelemetrySchemaSeed.All)
        {
            if (known.Contains(seed.Name)) continue;

            _dbContext.TelemetryEventSchemas.Add(new TelemetryEventSchema
            {
                Name = seed.Name,
                Group = seed.Group,
                Description = seed.Description,
                Category = seed.Category,
                SampleRate = seed.SampleRate,
                RetentionDays = seed.RetentionDays,
                Enabled = true,
                RollUpDaily = seed.RollUpDaily,
                Dimensions = seed.Dimensions,
                FirstSeenAtUtc = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            added++;
        }

        if (added > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _cache.Invalidate();
        }

        return added;
    }

    private static string NormaliseDimensions(string? raw, out string? unknown)
    {
        unknown = null;

        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var parts = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var part in parts)
        {
            if (TelemetryDimensions.All.Contains(part)) continue;

            unknown = part;
            return string.Empty;
        }

        return string.Join(',', parts);
    }

    private static string Trim(string? value, int max, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}

/// <summary>
/// The shipped vocabulary, with what each event is for.
/// <para>
/// **This table is the documentation.** Six years from now the person asking why a number moved
/// reads the console, and the console reads this — so a description that says "fired on lesson
/// complete" is worthless and one that says what question it answers is not.
/// </para>
/// </summary>
internal static class TelemetrySchemaSeed
{
    internal readonly record struct Seed(
        string Name,
        string Group,
        string Description,
        TelemetryCategory Category,
        string Dimensions = "",
        double SampleRate = 1.0,
        int? RetentionDays = null,
        bool RollUpDaily = true);

    private const string Plat = TelemetryDimensions.Platform;
    private const string Ver = TelemetryDimensions.AppVersion;
    private const string Game = TelemetryDimensions.GameId;
    private const string Loc = TelemetryDimensions.Locale;

    internal static readonly Seed[] All =
    [
        // ── session ──────────────────────────────────────────────────────
        // Operational: the boundaries of a visit are how the service is measured and debugged, not
        // a behavioural profile. They are also structural — the projector builds TelemetrySessions
        // from them, so a consent-gated session boundary would leave the rollups unable to count
        // anybody's play time.
        new(TelemetryNames.SessionStart, "session",
            "A play session began. The denominator of almost every engagement number.",
            TelemetryCategory.Operational, $"{Plat},{Ver}"),

        new(TelemetryNames.SessionEnd, "session",
            "A play session closed cleanly, carrying duration_s. Sessions with no end are crashes.",
            TelemetryCategory.Operational, $"{Plat},{Ver}"),

        new(TelemetryNames.AppLaunch, "session",
            "The process started. Separate from session_start so a cold launch is distinguishable.",
            TelemetryCategory.Operational, $"{Plat},{Ver}"),

        new(TelemetryNames.AppForeground, "session",
            "Returned from background. Pairs with app_background to explain a long session.",
            TelemetryCategory.Operational, RollUpDaily: false),

        new(TelemetryNames.AppBackground, "session",
            "Sent to background. The last reliable signal before the OS may kill the process.",
            TelemetryCategory.Operational, RollUpDaily: false),

        // ── navigation ───────────────────────────────────────────────────
        new(TelemetryNames.ScreenView, "navigation",
            "A screen was shown. The map of where children actually go in the app.",
            TelemetryCategory.Behavioural, Plat),

        new(TelemetryNames.ScreenExit, "navigation",
            "A screen was left, carrying dwell_ms. Where attention goes, and where it stalls.",
            TelemetryCategory.Behavioural),

        new(TelemetryNames.PopupOpen, "navigation", "A popup was opened.", TelemetryCategory.Behavioural),
        new(TelemetryNames.PopupClose, "navigation", "A popup was dismissed, carrying how.", TelemetryCategory.Behavioural),

        // ── onboarding ───────────────────────────────────────────────────
        // The highest-value funnel the platform has: everything downstream is conditional on it.
        new(TelemetryNames.OnboardingStep, "onboarding",
            "One step of first-launch setup. The funnel that decides whether anything else happens.",
            TelemetryCategory.Behavioural, $"{Plat},{Loc}", RetentionDays: 400),

        new(TelemetryNames.OnboardingComplete, "onboarding",
            "Setup finished. The conversion the acquisition number is measured against.",
            TelemetryCategory.Behavioural, $"{Plat},{Loc}", RetentionDays: 400),

        new(TelemetryNames.LanguageSelected, "onboarding",
            "A language was chosen at first launch, before any account exists.",
            TelemetryCategory.Behavioural, Loc),

        new(TelemetryNames.GradeSelected, "onboarding", "A grade was chosen.", TelemetryCategory.Behavioural),

        new(TelemetryNames.LoginSucceeded, "onboarding",
            "A session was established, carrying method — fresh, resumed or switched.",
            TelemetryCategory.Operational, Plat),

        new(TelemetryNames.LoginFailed, "onboarding",
            "Sign-in failed, carrying a stable reason token. An operational signal, not a product one.",
            TelemetryCategory.Operational, Plat),

        new(TelemetryNames.AccountSwitched, "onboarding",
            "Another account on the device was resumed. The shared-family-tablet signal.",
            TelemetryCategory.Operational),

        // ── learning ─────────────────────────────────────────────────────
        new(TelemetryNames.LessonStarted, "learning", "A lesson was entered.",
            TelemetryCategory.Behavioural, Game, RetentionDays: 400),

        new(TelemetryNames.LessonCompleted, "learning",
            "A lesson was finished, carrying correct/total and duration. The core learning outcome.",
            TelemetryCategory.Behavioural, Game, RetentionDays: 400),

        new(TelemetryNames.LessonAbandoned, "learning",
            "A lesson was left unfinished, carrying progress_pct. Where the curriculum loses children.",
            TelemetryCategory.Behavioural, Game, RetentionDays: 400),

        // Per-question, so it is the highest-volume learning event by an order of magnitude —
        // sampled, and not rolled up daily. Question-level analysis is a query over the raw window,
        // not a chart on the overview.
        new(TelemetryNames.QuestionAnswered, "learning",
            "One question was answered, carrying correct and time_ms. Sampled — high volume.",
            TelemetryCategory.Behavioural, SampleRate: 0.25, RollUpDaily: false),

        new(TelemetryNames.AttemptSubmitted, "learning",
            "An attempt reached the server. Structural: this is what TelemetryUserDay.AttemptCount counts.",
            TelemetryCategory.Operational, Game),

        new(TelemetryNames.NodeUnlocked, "learning", "A curriculum node opened up.", TelemetryCategory.Behavioural),

        // ── gameplay ─────────────────────────────────────────────────────
        new(TelemetryNames.RunStarted, "gameplay",
            "A mini-game run began. Structural: TelemetryUserDay.RunCount counts these.",
            TelemetryCategory.Operational, Game),

        new(TelemetryNames.RunEnded, "gameplay",
            "A run finished, carrying the reduced per-run summary — distance, jumps, obstacles, peak speed.",
            TelemetryCategory.Behavioural, Game),

        new(TelemetryNames.RunSettled, "gameplay",
            "The server settled the run. Carries whether a cap shortened the payout, never the payout.",
            TelemetryCategory.Operational, Game),

        new(TelemetryNames.PlayerDied, "gameplay",
            "A run ended in failure, carrying cause and distance. The difficulty curve, measured.",
            TelemetryCategory.Behavioural, Game),

        new(TelemetryNames.PowerUpUsed, "gameplay", "A power-up was consumed.", TelemetryCategory.Behavioural, Game),
        new(TelemetryNames.ReviveOffered, "gameplay", "A revive was offered.", TelemetryCategory.Behavioural, Game),
        new(TelemetryNames.ReviveTaken, "gameplay", "A revive was accepted.", TelemetryCategory.Behavioural, Game),

        // ── economy ──────────────────────────────────────────────────────
        // Context around a grant. The ledger owns the amounts — see Rule 2.
        new(TelemetryNames.ShopViewed, "economy", "A shop surface was opened.", TelemetryCategory.Behavioural),
        new(TelemetryNames.OfferViewed, "economy", "An offer was shown in enough detail to act on.", TelemetryCategory.Behavioural),
        new(TelemetryNames.PurchaseStarted, "economy",
            "A purchase request is about to be sent. The funnel's denominator, including attempts that never resolve.",
            TelemetryCategory.Behavioural),
        new(TelemetryNames.PurchaseSucceeded, "economy", "The server confirmed a transaction.", TelemetryCategory.Behavioural),
        new(TelemetryNames.PurchaseFailed, "economy", "The server refused, carrying a stable reason token.", TelemetryCategory.Behavioural),
        new(TelemetryNames.PurchaseUnknown, "economy",
            "An attempt ended with no known outcome. A reliability signal, not a conversion one.",
            TelemetryCategory.Operational),
        new(TelemetryNames.EntitlementGranted, "economy", "The account came to own something.", TelemetryCategory.Behavioural),

        new(TelemetryNames.CurrencyEarned, "economy",
            "Currency arrived, carrying source and amount. Context only — CurrencyLedgerEntries is the record.",
            TelemetryCategory.Operational),

        new(TelemetryNames.CurrencySpent, "economy",
            "Currency left, carrying sink and amount. Context only — the ledger is the record.",
            TelemetryCategory.Operational),

        new(TelemetryNames.RewardClaimed, "economy", "A reward rule paid out.", TelemetryCategory.Behavioural),

        new(TelemetryNames.EarnCapReached, "economy",
            "A daily earning ceiling shortened a payout. Rising counts mean the caps are mistuned.",
            TelemetryCategory.Operational),

        // ── progression ──────────────────────────────────────────────────
        new(TelemetryNames.LevelUp, "progression", "A level threshold was crossed.", TelemetryCategory.Behavioural),
        new(TelemetryNames.ObjectiveCompleted, "progression", "A quest or achievement completed.", TelemetryCategory.Behavioural),
        new(TelemetryNames.StreakExtended, "progression", "A consecutive-day streak grew.", TelemetryCategory.Behavioural),
        new(TelemetryNames.StreakBroken, "progression", "A streak lapsed. The churn warning that arrives before the churn.", TelemetryCategory.Behavioural),

        // ── multiplayer ──────────────────────────────────────────────────
        new(TelemetryNames.MatchmakingStarted, "multiplayer", "A search for a match began.", TelemetryCategory.Behavioural),
        new(TelemetryNames.MatchmakingMatched, "multiplayer", "A match was found, carrying wait_ms.", TelemetryCategory.Behavioural),
        new(TelemetryNames.MatchmakingCancelled, "multiplayer", "The search was abandoned, carrying wait_ms. Where the wait becomes too long.", TelemetryCategory.Behavioural),
        new(TelemetryNames.LobbyJoined, "multiplayer", "A lobby was entered.", TelemetryCategory.Behavioural),
        new(TelemetryNames.MatchFinished, "multiplayer", "A networked match ended, carrying placement and player count.", TelemetryCategory.Behavioural, Game),

        // ── advertising ──────────────────────────────────────────────────
        new(TelemetryNames.AdRequested, "ads", "An ad was requested for a placement.", TelemetryCategory.Behavioural),
        new(TelemetryNames.AdShown, "ads", "An ad was displayed.", TelemetryCategory.Behavioural),
        new(TelemetryNames.AdCompleted, "ads", "A rewarded ad ran to completion.", TelemetryCategory.Behavioural),
        new(TelemetryNames.AdFailed, "ads", "An ad failed to fill or to show.", TelemetryCategory.Operational),

        // ── operational ──────────────────────────────────────────────────
        new(TelemetryNames.ApiFailure, "operational",
            "A backend call failed, carrying endpoint key and status. First-party only, never a vendor sink.",
            TelemetryCategory.Operational, $"{Plat},{Ver}", RetentionDays: 30),

        new(TelemetryNames.SceneLoaded, "operational",
            "A scene finished loading, carrying ms. Sampled — every launch produces several.",
            TelemetryCategory.Operational, Plat, SampleRate: 0.2),

        new(TelemetryNames.DownloadFailed, "operational", "An addressable download failed.", TelemetryCategory.Operational, Plat),
        new(TelemetryNames.ErrorShown, "operational", "An error was rendered to a child, carrying its code.", TelemetryCategory.Operational),

        new(TelemetryNames.TelemetryQueueOverflow, "operational",
            "The client queue overflowed and dropped events, carrying how many. The honesty valve on every count.",
            TelemetryCategory.Operational, Plat)
    ];
}
