namespace Share7.Domain.Telemetry;

/// <summary>
/// The platform's event vocabulary, as constants.
/// <para>
/// **The client is the authority on these names and this is a mirror of it.** The Unity side
/// declares each one on an <c>AnalyticsEvent</c> subclass, where the compiler checks it; here they
/// exist so the seed can register them, the projector can special-case the handful that mean
/// something structural (session boundaries, runs, attempts), and a rename is a build error on
/// both sides rather than a metric that quietly stops.
/// </para>
/// <para>
/// **Stable forever once shipped.** A rename is a new metric with no history, not a correction.
/// Adding is free; renaming costs the series.
/// </para>
/// </summary>
public static class TelemetryNames
{
    // ── Session and lifecycle ────────────────────────────────────────────
    // Structural: the projector reads these to build TelemetrySessions.

    public const string SessionStart = "session_start";
    public const string SessionEnd = "session_end";
    public const string AppForeground = "app_foreground";
    public const string AppBackground = "app_background";
    public const string AppLaunch = "app_launch";

    // ── Navigation ───────────────────────────────────────────────────────

    public const string ScreenView = "screen_view";
    public const string ScreenExit = "screen_exit";
    public const string PopupOpen = "popup_open";
    public const string PopupClose = "popup_close";

    // ── Onboarding and account ───────────────────────────────────────────

    public const string OnboardingStep = "onboarding_step";
    public const string OnboardingComplete = "onboarding_complete";
    public const string LanguageSelected = "language_selected";
    public const string GradeSelected = "grade_selected";
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string AccountSwitched = "account_switched";

    // ── Learning ─────────────────────────────────────────────────────────

    public const string LessonStarted = "lesson_started";
    public const string LessonCompleted = "lesson_completed";
    public const string LessonAbandoned = "lesson_abandoned";
    public const string QuestionAnswered = "question_answered";
    public const string NodeUnlocked = "node_unlocked";

    /// <summary>An attempt was submitted to the server. Structural: counts into <c>TelemetryUserDay.AttemptCount</c>.</summary>
    public const string AttemptSubmitted = "attempt_submitted";

    // ── Mini-game ────────────────────────────────────────────────────────

    /// <summary>Structural: counts into <c>TelemetryUserDay.RunCount</c>.</summary>
    public const string RunStarted = "run_started";

    /// <summary>
    /// The per-run summary. **The one event the high-frequency gameplay channels reduce into** —
    /// see the bridge's note on being a reducer rather than a mirror.
    /// </summary>
    public const string RunEnded = "run_ended";

    public const string RunSettled = "run_settled";
    public const string PlayerDied = "player_died";
    public const string PowerUpUsed = "power_up_used";
    public const string ReviveOffered = "revive_offered";
    public const string ReviveTaken = "revive_taken";

    // ── Economy ──────────────────────────────────────────────────────────
    // Context around a grant, never the grant itself — the ledger owns that. See Rule 2.

    public const string ShopViewed = "shop_viewed";
    public const string OfferViewed = "offer_viewed";
    public const string PurchaseStarted = "purchase_started";
    public const string PurchaseSucceeded = "purchase_succeeded";
    public const string PurchaseFailed = "purchase_failed";
    public const string PurchaseUnknown = "purchase_unknown";
    public const string EntitlementGranted = "entitlement_granted";
    public const string CurrencyEarned = "currency_earned";
    public const string CurrencySpent = "currency_spent";
    public const string RewardClaimed = "reward_claimed";
    public const string EarnCapReached = "earn_cap_reached";

    // ── Progression ──────────────────────────────────────────────────────

    public const string LevelUp = "level_up";
    public const string ObjectiveCompleted = "objective_completed";
    public const string StreakExtended = "streak_extended";
    public const string StreakBroken = "streak_broken";

    // ── Multiplayer ──────────────────────────────────────────────────────

    public const string MatchmakingStarted = "matchmaking_started";
    public const string MatchmakingMatched = "matchmaking_matched";
    public const string MatchmakingCancelled = "matchmaking_cancelled";
    public const string LobbyJoined = "lobby_joined";
    public const string MatchFinished = "match_finished";

    // ── Advertising ──────────────────────────────────────────────────────

    public const string AdRequested = "ad_requested";
    public const string AdShown = "ad_shown";
    public const string AdCompleted = "ad_completed";
    public const string AdFailed = "ad_failed";

    // ── Operational ──────────────────────────────────────────────────────
    // Not consent-gated; first-party only. See TelemetryCategory.

    public const string ApiFailure = "api_failure";
    public const string SceneLoaded = "scene_loaded";
    public const string DownloadFailed = "download_failed";
    public const string ErrorShown = "error_shown";
    public const string TelemetryQueueOverflow = "telemetry_queue_overflow";

    /// <summary>Column width, and the client's own limit. Several SDKs truncate around 40.</summary>
    public const int MaxNameLength = 64;
}
