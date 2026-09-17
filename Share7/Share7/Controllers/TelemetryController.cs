using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Share7.API.Extensions;
using Share7.API.RateLimiting;
using Share7.Application.Common.Interfaces;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;

namespace Share7.API.Controllers;

/// <summary>
/// Where the client reports what a player did.
/// <para>
/// **There is no user id in the request body, and no field a client could put one in.** Identity
/// comes from the bearer token on the request that carried the batch. That absence is the contract:
/// the payload stays free of identifiers for a vendor sink that may exist one day, a modified build
/// cannot attribute its events to another child, and the event vocabulary needs no exception to its
/// own authoring rule. See <c>Docs/AnalyticsArchitecture.md</c> → Rule 1.
/// </para>
/// <para>
/// **Nothing here can move a balance or change progress.** Telemetry records context around what
/// the platform did; the ledgers record what it did. A grant appears on a child's trace because it
/// is in <c>CurrencyLedgerEntries</c>, not because a client also reported it — which is why there
/// is no request shape here that names a currency, an amount or an entitlement.
/// </para>
/// </summary>
[ApiController]
[Route("api/telemetry")]
[Authorize]
public class TelemetryController : ControllerBase
{
    private readonly ITelemetryIngestService _ingest;
    private readonly ICurrentUserService _currentUser;

    public TelemetryController(ITelemetryIngestService ingest, ICurrentUserService currentUser)
    {
        _ingest = ingest;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Records a batch of events.
    /// <code>
    /// {
    ///   "sessionId": "…",
    ///   "context": { "appVersion": "1.4.2", "platform": "android",
    ///                "deviceModel": "SM-A536B", "locale": "ar" },
    ///   "events": [
    ///     { "id": "…", "name": "lesson_completed", "occurredAtUtc": "…", "clientSeq": 41,
    ///       "gameId": "…", "runId": null, "sampleRate": 1.0,
    ///       "params": { "correct": 8, "total": 10, "duration_ms": 94500 } }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// **Partial success is the normal outcome, not an error.** Valid events are stored even when
    /// others in the same batch are refused, and the response says which were which. Failing the
    /// whole batch would let one malformed event on a shipped build block every event queued behind
    /// it — permanently, because the client would retry the same batch forever.
    /// </para>
    /// <para>
    /// Idempotent on each event's <c>id</c>: re-sending a batch after a dropped connection stores
    /// nothing twice and reports the repeats as <c>duplicates</c>. The offline queue retries on
    /// reconnect by design, so a replay is the ordinary path.
    /// </para>
    /// <para>
    /// **The response steers the client.** <c>maxBatchSize</c>, <c>retryAfterSeconds</c> and
    /// <c>sampling</c> are how a chatty event is turned down or a struggling server is given room —
    /// on the next batch, rather than at the next release. Apply them; do not cache them past the
    /// following request.
    /// </para>
    /// <para>
    /// <c>rejected</c> entries carry a stable reason token. **Drop them** — a rejection is a
    /// statement about the event, and retrying one forever is a queue that never drains.
    /// </para>
    /// </summary>
    /// <response code="400">The batch was malformed, empty, or larger than <c>maxBatchSize</c>.</response>
    /// <response code="429">Rate limited. Honour <c>Retry-After</c>.</response>
    [HttpPost("events")]
    [EnableRateLimiting(RateLimitPolicies.Telemetry)]
    public async Task<IActionResult> Submit(TelemetryBatchRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await _ingest.IngestAsync(userId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }
}
