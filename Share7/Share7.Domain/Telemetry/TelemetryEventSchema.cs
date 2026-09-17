namespace Share7.Domain.Telemetry;

/// <summary>
/// One registered event name, and everything the pipeline needs to know about it that is not in
/// the event itself.
/// <para>
/// **This table is what stops ten years becoming four thousand event names.** Without a registry
/// every typo, every abandoned experiment and every mini-game's private vocabulary accumulates in
/// the same stream, and the person trying to answer a product question in year six cannot tell
/// which of six similar names is the one still being emitted. Registration is cheap; an
/// unregistered stream is not recoverable.
/// </para>
/// <para>
/// Authored data, like <c>SignalValuation</c> and <c>LeaderboardMetricBound</c>: turning an event
/// down to 5% sampling or shortening its retention is a row edit, not a client release.
/// </para>
/// </summary>
public class TelemetryEventSchema
{
    /// <summary>The event's wire name. The key — a name cannot be registered twice.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Grouping for the console only — <c>session</c>, <c>learning</c>, <c>economy</c>,
    /// <c>gameplay</c>. Not a lawful basis; that is <see cref="Category"/>.
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>What the event means, in one line, for whoever reads the console in year six.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The lawful basis this event is collected under. Copied onto each event at ingest — see
    /// <see cref="TelemetryEvent.Category"/> for why it is not read through a join.
    /// </summary>
    public TelemetryCategory Category { get; set; } = TelemetryCategory.Behavioural;

    /// <summary>
    /// Fraction of occurrences the client should send, <c>0.0</c>–<c>1.0</c>.
    /// <para>
    /// Returned in the ingest response so a chatty event can be turned down without a release. The
    /// client stamps the rate it actually used onto each event, so a count sampled at 5% can be
    /// scaled back up honestly rather than read as a collapse in usage.
    /// </para>
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// Days to keep raw rows of this event, or null to use the category default.
    /// <para>
    /// An override exists because the two ends of the range are both real: an <c>fps_sample</c> is
    /// worthless after a fortnight, and the handful of events a launch funnel is built on are worth
    /// four hundred days so that next year's launch has something to compare against.
    /// </para>
    /// </summary>
    public int? RetentionDays { get; set; }

    /// <summary>
    /// Whether the pipeline accepts this event at all.
    /// <para>
    /// Disabling refuses it at ingest and tells the client why, so the client stops sending it —
    /// which is the point. Silently accepting and discarding would leave a shipped build spending
    /// a child's battery and data on rows nobody stores.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the projector folds this event into <see cref="TelemetryDailyMetric"/>.
    /// <para>
    /// Off for the very high-volume ones. A daily counter for an event that fires thirty times a
    /// session tells you nothing a session count does not, and it is a row per dimension value per
    /// day that somebody has to store forever.
    /// </para>
    /// </summary>
    public bool RollUpDaily { get; set; } = true;

    /// <summary>
    /// Dimensions to split the daily counter by, comma-separated — <c>platform,app_version</c>.
    /// Empty means the ungrouped total only.
    /// <para>
    /// Authored per event, because cardinality is per event: splitting <c>session_start</c> by
    /// platform is four rows a day and splitting anything by a parameter with a thousand values is
    /// a thousand.
    /// </para>
    /// </summary>
    public string Dimensions { get; set; } = string.Empty;

    /// <summary>
    /// Set when the name first arrived without a registration, so the console can offer it for
    /// review. Null on anything an operator authored deliberately.
    /// </summary>
    public DateTime? FirstSeenAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Dimension names a schema's <see cref="TelemetryEventSchema.Dimensions"/> may name. Constants,
/// so a typo in authored data is caught by the one place that resolves them rather than producing
/// a rollup row nobody ever queries.
/// </summary>
public static class TelemetryDimensions
{
    public const string Platform = "platform";
    public const string AppVersion = "app_version";
    public const string GameId = "game_id";
    public const string Locale = "locale";

    public static readonly string[] All = [Platform, AppVersion, GameId, Locale];

    public const int MaxLength = 32;
}
