using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Share7.Application.Telemetry.Models;

// ---- what the client sends -------------------------------------------------------------------

/// <summary>
/// One batch of events from one client session.
/// <para>
/// **There is no user id in this type, and there is no field a client could put one in.** The
/// server stamps identity from the bearer token on the request that carried the batch. That is
/// three guarantees in one absence: the payload stays free of identifiers for a vendor sink that
/// may exist one day, a modified build cannot attribute its events to another child, and the
/// event vocabulary needs no exception to its own authoring rule. See
/// <c>Docs/AnalyticsArchitecture.md</c> → Rule 1.
/// </para>
/// </summary>
public class TelemetryBatchRequest
{
    /// <summary>
    /// The client's play session. A client concept — there is no server session to correlate with,
    /// and one invented here would measure how long a token lived rather than how long a child played.
    /// </summary>
    [Required]
    public Guid SessionId { get; set; }

    [Required]
    public TelemetryContextDto Context { get; set; } = new();

    [Required]
    [MinLength(1)]
    public List<TelemetryEventDto> Events { get; set; } = [];
}

/// <summary>
/// What the client is, repeated once per batch rather than once per event.
/// <para>
/// Per batch because it does not change within one: the alternative is the same four strings on
/// every row of the wire, which at a hundred events a batch is most of the payload.
/// </para>
/// </summary>
public class TelemetryContextDto
{
    [Required]
    [MaxLength(32)]
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>See <c>TelemetryPlatforms</c>. Free text, so an unknown platform stays legible.</summary>
    [Required]
    [MaxLength(16)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Device **class** — <c>SM-A536B</c>. Not a device identifier, and the denylist refuses
    /// anything that is one. This answers "did the frame rate collapse on low-end Androids".
    /// </summary>
    [MaxLength(64)]
    public string? DeviceModel { get; set; }

    [MaxLength(16)]
    public string? Locale { get; set; }
}

/// <summary>One event. Everything it means is fixed when the client constructs it.</summary>
public class TelemetryEventDto
{
    /// <summary>
    /// The client's id for this occurrence, and the idempotency key. Client-generated because the
    /// offline queue retries by design and only the client knows two deliveries are one event.
    /// </summary>
    [Required]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Clamped server-side into the backlog window. See <c>TelemetryEvent.OccurredAtUtc</c>.</summary>
    [Required]
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Per-session ordering as the client saw it. A gap here is a dropped event, which is itself a signal.</summary>
    public int ClientSeq { get; set; }

    public Guid? GameId { get; set; }

    /// <summary>Correlates to the authoritative <c>Run</c>, when there is one.</summary>
    public Guid? RunId { get; set; }

    /// <summary>
    /// The rate the client sampled this event at, <c>1.0</c> when it sent everything.
    /// <para>
    /// Sent rather than looked up server-side, because the registry's rate is whatever it is today
    /// and this event was sampled under whatever it was then. Without it a rate change reads as a
    /// collapse in usage.
    /// </para>
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// The flat parameter bag. Scalars only — string, number, boolean.
    /// <para>
    /// <c>JsonElement</c> rather than <c>object</c> so the values arrive with their JSON types
    /// intact and a <c>"1"</c> written as a string is not silently re-typed into a number on the
    /// way through. Nested objects and arrays are refused: a bag that can nest is a schema nobody
    /// declared, and it is what turns an event stream into an unqueryable pile in year three.
    /// </para>
    /// </summary>
    public Dictionary<string, JsonElement>? Params { get; set; }
}

// ---- what the server answers -----------------------------------------------------------------

/// <summary>
/// The outcome of a batch, and **the channel the server steers the client with**.
/// <para>
/// Batch size, backoff and per-event sampling all arrive here rather than in a config the client
/// ships with. A chatty event discovered in production is then a row edit that takes effect on the
/// next batch, instead of a release that takes a fortnight to reach the devices causing the load.
/// </para>
/// </summary>
public class TelemetryBatchResponse
{
    /// <summary>Events stored by this request.</summary>
    public int Accepted { get; init; }

    /// <summary>
    /// Events already stored under the same id. **Not an error** — the offline queue retries on
    /// reconnect by design, so a replay is the ordinary path. Counted separately so a rise in it is
    /// still visible as a client that is failing to drain its queue.
    /// </summary>
    public int Duplicates { get; init; }

    /// <summary>
    /// Events refused, with a stable reason each. The client **drops** these rather than retrying:
    /// a rejection is a statement about the event, and retrying it forever is a queue that never
    /// drains.
    /// </summary>
    public IReadOnlyList<TelemetryRejectionDto> Rejected { get; init; } = [];

    /// <summary>Current server limit. The client resizes its next batch to this.</summary>
    public int MaxBatchSize { get; init; }

    /// <summary>Set when the server wants the client to back off. Honoured on top of the client's own backoff.</summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>
    /// Per-event sampling rates the client should apply, for the names it just sent. Only names
    /// whose rate is below <c>1.0</c> appear — the common case is an empty object.
    /// </summary>
    public IReadOnlyDictionary<string, double> Sampling { get; init; } =
        new Dictionary<string, double>();
}

/// <summary>One refused event. <c>Reason</c> is a stable token from <c>TelemetryRejectReasons</c>.</summary>
public class TelemetryRejectionDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    /// <summary>Human detail for the console. Never rendered to a child, and never a stable key.</summary>
    public string? Detail { get; init; }
}
