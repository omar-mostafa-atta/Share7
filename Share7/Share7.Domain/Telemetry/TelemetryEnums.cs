namespace Share7.Domain.Telemetry;

/// <summary>
/// What lawful basis an event is collected under, and therefore where it may go and how long it
/// is kept.
/// <para>
/// **This is not a severity level and it is not a namespace.** It is the answer to "why are we
/// allowed to have this row", and the users are children — so the answer has to be decided when
/// the event is authored, not inferred later by whoever is writing a purge script. See
/// <c>Docs/AnalyticsArchitecture.md</c> → Rule 3.
/// </para>
/// </summary>
public enum TelemetryCategory
{
    Unknown = 0,

    /// <summary>
    /// Needed to deliver and debug the service the account exists to provide — API failures, load
    /// times, session boundaries, the context around an economy grant.
    /// <para>
    /// **Not consent-gated, and first-party only.** "Strictly necessary for our service" is a real
    /// basis and this is what it covers; it stops being true the moment the data leaves for
    /// somebody else's, so no vendor sink ever receives one of these.
    /// </para>
    /// </summary>
    Operational = 1,

    /// <summary>
    /// Product analytics — funnels, screen dwell, offer views, feature usage. Collected only while
    /// <c>AnalyticsConsentState.Granted</c>, and dropped rather than queued otherwise.
    /// </summary>
    Behavioural = 2
}

/// <summary>
/// Which surface an event came from. A dimension, not a security boundary — the client says what
/// it is running on and there is nothing to check it against.
/// <para>
/// Stored as text rather than parsed into an enum: a platform this does not know about is a fact
/// worth keeping, and an enum would turn it into <c>Unknown</c> and lose which one it was.
/// </para>
/// </summary>
public static class TelemetryPlatforms
{
    public const string Android = "android";
    public const string Ios = "ios";
    public const string Editor = "editor";
    public const string Standalone = "standalone";
    public const string WebGl = "webgl";

    /// <summary>Column width. Long enough for anything Unity reports, short enough to index.</summary>
    public const int MaxLength = 16;
}

/// <summary>
/// Why an event in a submitted batch was not stored. Returned to the client so it can drop the
/// event from its queue instead of retrying it forever.
/// <para>
/// Stable tokens, not prose: the client logs them and the console groups by them, and a reworded
/// message would split one problem into two on every dashboard that counts them.
/// </para>
/// </summary>
public static class TelemetryRejectReasons
{
    /// <summary>Name was blank, too long, or not <c>snake_case</c>.</summary>
    public const string InvalidName = "invalid_name";

    /// <summary>A parameter key matched the identifier denylist. See <c>TelemetryPrivacy</c>.</summary>
    public const string ForbiddenParam = "forbidden_param";

    /// <summary>The parameter blob exceeded the column budget.</summary>
    public const string PayloadTooLarge = "payload_too_large";

    /// <summary>The registry has this name disabled. Authored refusal, not a fault.</summary>
    public const string SchemaDisabled = "schema_disabled";

    /// <summary>Too many distinct unregistered names in one batch — a broken build, not a feature.</summary>
    public const string UnregisteredFlood = "unregistered_flood";

    /// <summary>Malformed id, timestamp or session id.</summary>
    public const string Malformed = "malformed";
}
