namespace Share7.Domain.Telemetry;

/// <summary>
/// One thing a player did, as reported by their client. **Append-only, and never the source of
/// truth for anything the platform gave them.**
/// <para>
/// The distinction is the whole design. <c>CurrencyLedgerEntries</c>, <c>RewardTransactions</c>,
/// <c>PurchaseTransactions</c> and <c>RunPayouts</c> already record every grant authoritatively;
/// this table records the *context* around one — which screen, how long, what was on offer. A
/// telemetry row that also carried a balance would be a second record of what a child owns,
/// derived from a client that is explicitly not authoritative about it, and somebody would
/// eventually reconcile against it. See <c>Docs/AnalyticsArchitecture.md</c> → Rule 2.
/// </para>
/// <para>
/// **Nothing here comes from the payload except what the payload is for.** <see cref="UserId"/> is
/// stamped from the bearer token, never read from the body: a client-supplied user id is a
/// client-supplied claim, and the same absence is what keeps the event vocabulary free of
/// identifiers for a vendor sink that might exist one day.
/// </para>
/// </summary>
public class TelemetryEvent
{
    /// <summary>
    /// Insertion order, and the projector's cursor. **Not a clock** — two events written in the
    /// same millisecond cannot be separated by one, which is the reason
    /// <c>ProjectionCheckpoint.Watermark</c> is a sequence everywhere else in this schema too.
    /// </summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The client's id for this event, and the idempotency key. **Client-generated on purpose:**
    /// the offline queue retries on reconnect by design, so the same event arriving twice is the
    /// ordinary path rather than an anomaly, and only the client can know that two deliveries are
    /// one occurrence.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Whose event it is, taken from the authenticated request. Cascades with the account, like
    /// <c>Runs</c> — so this table needs no entry in <c>UserOwnedData.ManuallyPurged</c>.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The client play session this belongs to. **A client concept, not a server session** — there
    /// is no server-side session to correlate with, and inventing one would only measure how long
    /// a token lived rather than how long a child played.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>The event's wire name, <c>snake_case</c>. Joins to <see cref="TelemetryEventSchema"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// See <see cref="TelemetryCategory"/>. Copied from the registry at ingest rather than read
    /// through a join later, so a category changed in the registry next year does not retroactively
    /// re-classify events collected under the old basis — which is the one thing a lawful basis
    /// must never do.
    /// </summary>
    public TelemetryCategory Category { get; set; } = TelemetryCategory.Behavioural;

    /// <summary>
    /// When the client says it happened, **clamped into <c>[Received - MaxBacklogDays, Received]</c>**.
    /// <para>
    /// The clamp is not tidying. A child's tablet clock can be years out — the same problem
    /// <c>Run.DurationMs</c> already clamps for — and an unclamped value drops events into 2019,
    /// silently corrupting every cohort they land in. Kept because ordering *within* a session and
    /// showing a human when something happened both need it.
    /// </para>
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// When the server received it. The honest clock, and what every rollup keys on. Also what the
    /// projector's safety lag is measured against.
    /// </summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>
    /// The UTC day of <see cref="ReceivedAtUtc"/>, stored rather than derived.
    /// <para>
    /// Every rollup, every retention sweep and every dashboard range filters on a day. Computing it
    /// in the predicate makes the expression non-sargable and every one of those queries a scan of
    /// the largest table in the system.
    /// </para>
    /// </summary>
    public DateTime DayUtc { get; set; }

    /// <summary>
    /// Per-session ordering as the client saw it. Survives two events in the same millisecond,
    /// which <see cref="OccurredAtUtc"/> does not, and makes a gap in a session visible — an event
    /// that was dropped by the queue leaves a hole in this sequence.
    /// </summary>
    public int ClientSeq { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    /// <summary>See <see cref="TelemetryPlatforms"/>. Text, so an unknown platform stays legible.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Device class — <c>SM-A536B</c>, <c>iPhone14,5</c>. **A model, not an identifier**: it says
    /// what hardware the experience ran on, which is what a frame-rate or load-time question needs,
    /// and it is shared by millions of devices.
    /// </summary>
    public string? DeviceModel { get; set; }

    /// <summary>Locale as the client reports it. A dimension for content questions, not a preference.</summary>
    public string? Locale { get; set; }

    /// <summary>Which mini-game, when the event has one. A dimension on the rollups.</summary>
    public Guid? GameId { get; set; }

    /// <summary>
    /// The authoritative <c>Run</c> this event belongs to, when there is one.
    /// <para>
    /// **Deliberately not a foreign key.** It is a correlation pointer, and telemetry has to stay
    /// readable after a run has been swept — the same reasoning <c>Run.SessionId</c> is documented
    /// with. A key here would either block the sweep or cascade away the trace.
    /// </para>
    /// </summary>
    public Guid? RunId { get; set; }

    /// <summary>
    /// The event's parameters as a flat JSON object, verbatim after scrubbing.
    /// <para>
    /// **JSON and not an EAV child table.** Ingest has to be one insert per batch to survive a
    /// million concurrent players; a parameter table makes it one insert per *parameter*, which is
    /// an order of magnitude more rows and a second index to maintain on the hottest write path in
    /// the platform. Nothing reads parameters in bulk — dashboards read rollups (Rule 4) and only
    /// the timeline and the event explorer open a payload, both over a bounded set of rows.
    /// </para>
    /// </summary>
    public string ParamsJson { get; set; } = "{}";

    /// <summary>
    /// The sampling rate the client applied, <c>1.0</c> when it sent everything.
    /// <para>
    /// Recorded per event rather than looked up from the registry, because the registry's rate is
    /// whatever it is *today* and this event was sampled under whatever it was then. Without it a
    /// rate change reads as a collapse in usage, and the count can never be honestly scaled back up.
    /// </para>
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// The registry had never heard of this name.
    /// <para>
    /// **Stored anyway, and never rolled up.** Refusing it would lose real data whenever a client
    /// ships ahead of a registry row, which is the ordinary order of a release; folding it would let
    /// a typo create a metric. So it waits in the console for a human to register it, and the
    /// rollups stay clean in the meantime. See Rule 6.
    /// </para>
    /// </summary>
    public bool IsUnregistered { get; set; }
}
