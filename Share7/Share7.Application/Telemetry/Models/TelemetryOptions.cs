namespace Share7.Application.Telemetry.Models;

/// <summary>
/// Every tunable in the telemetry pipeline, bound from the <c>Telemetry</c> configuration section.
/// <para>
/// Configuration rather than constants for the same reason <c>RateLimitOptions</c> is: the right
/// numbers are operational questions that depend on how chatty the shipped client turns out to be,
/// and finding that out should not need a redeploy. The defaults here are sized for a launch, not
/// for a million DAU — the ones that matter at scale are called out individually.
/// </para>
/// </summary>
public class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    /// Master switch. On by default — an off-by-default pipeline collects nothing and nobody
    /// notices for a month — but present so ingest can be shed under load without a build.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // ── Ingest ───────────────────────────────────────────────────────────

    /// <summary>
    /// Most events one request may carry. Returned to the client in every response, so lowering it
    /// takes effect on the next batch rather than at the next release.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Longest a client may backdate an event, in days. Anything older is clamped forward, not
    /// refused — a tablet that was offline for a fortnight has real events on it.
    /// <para>
    /// The clamp is what stops a wrong device clock corrupting a cohort. See
    /// <c>TelemetryEvent.OccurredAtUtc</c>.
    /// </para>
    /// </summary>
    public int MaxBacklogDays { get; set; } = 14;

    /// <summary>
    /// Bytes of serialised parameters one event may carry. Matched to the column width, so an
    /// oversized payload is a clear rejection rather than a truncation nobody notices.
    /// </summary>
    public int MaxParamsBytes { get; set; } = 2048;

    /// <summary>Most parameters one event may carry. A bag with fifty keys is a design mistake, not a payload.</summary>
    public int MaxParamsPerEvent { get; set; } = 24;

    /// <summary>
    /// Distinct unregistered event names one batch may introduce before the rest are refused.
    /// <para>
    /// The guard against a broken build filling the store with garbage names. Low, because a real
    /// release adds a handful of events at once and never a hundred.
    /// </para>
    /// </summary>
    public int MaxUnregisteredNamesPerBatch { get; set; } = 5;

    // ── Projector ────────────────────────────────────────────────────────

    /// <summary>
    /// How long a row must have been committed before the projector will read it.
    /// <para>
    /// **This is the identity-gap guard and it is load-bearing.** <c>Sequence</c> is an identity
    /// column, so 100 can commit while 99 is still in flight; a projector that watermarks at
    /// <c>MAX(Sequence)</c> skips 99 permanently. The lag has to exceed the longest ingest
    /// transaction, and ingest is one insert — thirty seconds is orders of magnitude of headroom.
    /// </para>
    /// </summary>
    public int SafetyLagSeconds { get; set; } = 30;

    /// <summary>Events folded per projector pass. Bounded so one pass cannot hold the tables all night.</summary>
    public int ProjectionBatchSize { get; set; } = 2000;

    /// <summary>Passes per tick before yielding, so a backlog drains over one tick rather than one batch per tick.</summary>
    public int MaxProjectionPassesPerTick { get; set; } = 20;

    /// <summary>Seconds between projector ticks. Sub-minute so the console is near-live, not real-time.</summary>
    public int ProjectionIntervalSeconds { get; set; } = 20;

    // ── Rollups ──────────────────────────────────────────────────────────

    /// <summary>
    /// Hours between nightly passes — cohort rebuild and unique-user recompute. Twelve rather than
    /// twenty-four so a missed run costs half a day of freshness on tables that are cheap to redo.
    /// </summary>
    public int NightlyIntervalHours { get; set; } = 12;

    /// <summary>
    /// Furthest day index the cohort triangle is computed to. 90 covers D1/D7/D30 with room; going
    /// to 365 multiplies the cells by four for a question almost nobody asks of a live game.
    /// </summary>
    public int MaxCohortDayIndex { get; set; } = 90;

    /// <summary>
    /// How far back a nightly pass recomputes. A late-arriving batch can add activity to a day
    /// already summarised, so the recent window is always redone rather than trusted.
    /// </summary>
    public int NightlyLookbackDays { get; set; } = 35;

    // ── Retention ────────────────────────────────────────────────────────

    /// <summary>
    /// Days of raw <c>Behavioural</c> events kept when a schema names no override.
    /// **Rollups are never swept** — they are what makes a ten-year-old cohort still answerable.
    /// </summary>
    public int BehaviouralRetentionDays { get; set; } = 90;

    /// <summary>Days of raw <c>Operational</c> events kept. Shorter: these answer "is it broken now".</summary>
    public int OperationalRetentionDays { get; set; } = 30;

    /// <summary>Rows deleted per sweep statement. Bounded to keep the delete off the lock escalation threshold.</summary>
    public int RetentionBatchSize { get; set; } = 5000;

    /// <summary>Sweeps per tick before yielding. Same bounded-drain shape as <c>GameResultRetentionSweeper</c>.</summary>
    public int MaxRetentionPassesPerTick { get; set; } = 20;

    /// <summary>Minutes between retention sweeps.</summary>
    public int RetentionIntervalMinutes { get; set; } = 60;

    // ── Query ────────────────────────────────────────────────────────────

    /// <summary>Most timeline entries one page may return. The console pages; a bulk export is a different feature.</summary>
    public int MaxTimelinePageSize { get; set; } = 200;

    /// <summary>
    /// Longest range, in days, an admin query may cover. Guards the console against asking for a
    /// three-year scan by typing a wrong date, which at scale is an outage rather than a slow page.
    /// </summary>
    public int MaxQueryRangeDays { get; set; } = 400;
}
