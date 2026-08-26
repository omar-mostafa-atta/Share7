using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Share7.API.Extensions;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Constants;

namespace Share7.API.Controllers;

/// <summary>
/// The analytics console's read surface.
/// <para>
/// **Every route here reads a rollup or a ledger.** Nothing scans the raw event table except the
/// two that say so on themselves — the funnel and the per-event parameter breakdown — and both are
/// bounded. When a new question needs a scan, the answer is a new rollup rather than a slower
/// endpoint; that trade is the reason the dashboards still work at a million daily players. See
/// <c>Docs/AnalyticsArchitecture.md</c> → Rule 4.
/// </para>
/// <para>
/// Ranges are UTC days and inclusive at both ends. Omit them and you get the last thirty days.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
public class AdminAnalyticsController : ControllerBase
{
    private readonly ITelemetryAnalyticsService _analytics;
    private readonly ITelemetrySchemaService _schemas;
    private readonly IUserTimelineService _timeline;

    public AdminAnalyticsController(
        ITelemetryAnalyticsService analytics,
        ITelemetrySchemaService schemas,
        IUserTimelineService timeline)
    {
        _analytics = analytics;
        _schemas = schemas;
        _timeline = timeline;
    }

    /// <summary>
    /// The headline numbers: DAU/WAU/MAU, new users, sessions, play time, and the D1/D7/D30 figures.
    /// <para>
    /// **The retention headlines are null until their cohorts have matured**, and the console must
    /// render that as "not yet" rather than as zero — a D30 computed from cohorts eleven days old
    /// is not a low number, it is a meaningless one.
    /// </para>
    /// <para>
    /// <c>projectionLagSeconds</c> and <c>pendingEvents</c> are here because a stalled projector
    /// looks exactly like a collapse in engagement — every figure goes flat — and this pair is the
    /// only thing that tells them apart.
    /// </para>
    /// </summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var result = await _analytics.GetOverviewAsync(start, end, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The cohort triangle, plus the weighted curve under it.
    /// <para>
    /// A cohort's <c>cells</c> array stops at the day index it has actually aged to. **A missing
    /// cell means "not yet known", not zero** — rendering the gap as zero draws a cliff that is
    /// really just today's date.
    /// </para>
    /// </summary>
    [HttpGet("retention")]
    public async Task<IActionResult> Retention(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int maxDayIndex = 30,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = Range(from, to);
        var result = await _analytics.GetRetentionAsync(start, end, maxDayIndex, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// A daily series for one metric, optionally split by a dimension.
    /// <para>
    /// <c>metric</c> is an event name, or one of two synthetic series that are not event counts at
    /// all: <c>active_users</c> and <c>new_users</c>, which read the user-day rollup because it is
    /// the only place a distinct-user-per-day figure exists.
    /// </para>
    /// <para>
    /// A point's <c>uniqueUsers</c> is null until the nightly pass has computed it. Render pending,
    /// never zero.
    /// </para>
    /// </summary>
    [HttpGet("timeseries")]
    public async Task<IActionResult> Timeseries(
        [FromQuery] string metric,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? dimension,
        CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var result = await _analytics.GetTimeseriesAsync(metric, start, end, dimension, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The event registry with volumes, and separately the names seen in the wild that nobody has
    /// registered.
    /// <para>
    /// That second list is the queue that keeps the vocabulary honest: an unregistered event is
    /// stored but never folded into a rollup, so it produces no metric until somebody says what it
    /// is. Working through it is maintenance, not cleanup.
    /// </para>
    /// </summary>
    [HttpGet("events")]
    public async Task<IActionResult> Events(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var result = await _analytics.GetEventCatalogueAsync(start, end, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// One event's daily volume and what its parameters actually contain.
    /// <para>
    /// The parameter breakdown is computed from a **bounded sample** of recent rows and reports its
    /// own <c>sampleSize</c>. It answers "what does this event look like", which is a shape
    /// question; it is not a metric and must not be charted as one.
    /// </para>
    /// </summary>
    [HttpGet("events/{name}")]
    public async Task<IActionResult> EventDetail(
        string name, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var result = await _analytics.GetEventDetailAsync(name, start, end, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Conversion through ordered steps: <c>?steps=shop_viewed,offer_viewed,purchase_succeeded</c>.
    /// <para>
    /// **Counted per user and in order, within <c>windowHours</c> of their first step.** A funnel
    /// counted on occurrences lets one child who opened the shop forty times look like forty
    /// children; one counted without ordering is an intersection of populations, which always looks
    /// healthier than the journey really is.
    /// </para>
    /// </summary>
    [HttpGet("funnel")]
    public async Task<IActionResult> Funnel(
        [FromQuery] string steps,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int windowHours = 24,
        CancellationToken cancellationToken = default)
    {
        var parsed = (steps ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var (start, end) = Range(from, to);
        var result = await _analytics.GetFunnelAsync(parsed, start, end, windowHours, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Virtual currency in and out, per currency and per day.
    /// <para>
    /// **From <c>CurrencyLedgerEntries</c>, not from telemetry.** The ledger is the authoritative
    /// record of every credit and debit; a second count assembled from client events would
    /// eventually disagree with it, and the ledger would be right.
    /// </para>
    /// <para>
    /// Sustained positive <c>net</c> is inflation: the economy is minting faster than it removes,
    /// and every price in the shop is quietly getting cheaper.
    /// </para>
    /// </summary>
    [HttpGet("economy")]
    public async Task<IActionResult> Economy(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var (start, end) = Range(from, to);
        var result = await _analytics.GetEconomyAsync(start, end, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>The user-360 header: lifecycle, totals, balances and lifetime currency flow.</summary>
    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> UserProfile(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _timeline.GetProfileAsync(userId, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// The full trace: every event, grant, reward, purchase, entitlement, run and graded attempt
    /// for one account, newest first.
    /// <para>
    /// Page with <c>before</c> — pass back the <c>nextBeforeUtc</c> from the previous page. It is a
    /// timestamp rather than an offset because the trace merges seven independently ordered
    /// sources, and an offset into a merged list shifts the moment any one of them gains a row.
    /// </para>
    /// <para>
    /// Filter with <c>sources=CurrencyLedger,Reward</c> to answer an economy question without the
    /// behavioural noise around it.
    /// </para>
    /// </summary>
    [HttpGet("users/{userId:guid}/timeline")]
    public async Task<IActionResult> UserTimeline(
        Guid userId,
        [FromQuery] DateTime? before,
        [FromQuery] int limit = 50,
        [FromQuery] string? sources = null,
        CancellationToken cancellationToken = default)
    {
        List<TimelineSourceKind>? kinds = null;

        if (!string.IsNullOrWhiteSpace(sources))
        {
            kinds = [];

            foreach (var token in sources.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<TimelineSourceKind>(token.Trim(), ignoreCase: true, out var kind))
                    kinds.Add(kind);
            }

            // An all-unrecognised filter means "nothing matched", which would silently return the
            // unfiltered trace if it collapsed back to null. Refuse instead of answering a
            // different question than the one asked.
            if (kinds.Count == 0)
                return BadRequest(new { errors = new[] { $"No recognised source in '{sources}'." } });
        }

        var result = await _timeline.GetTimelineAsync(userId, before, limit, kinds, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Registers a name, or re-authors one that already exists.
    /// <para>
    /// Registering an unrecognised name takes it out of the review queue and starts folding it into
    /// rollups from the next projector pass. **Events already stored stay unfolded** — backfilling
    /// them would mean rewinding the watermark for every consumer, and a metric that begins on the
    /// day it was registered is at least honest about itself.
    /// </para>
    /// </summary>
    [HttpPut("schemas/{name}")]
    public async Task<IActionResult> UpsertSchema(
        string name, UpsertEventSchemaRequest request, CancellationToken cancellationToken)
    {
        var result = await _schemas.UpsertAsync(name, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : result.ToApiErrorResult();
    }

    /// <summary>
    /// Re-runs the vocabulary seed.
    /// <para>
    /// Additive only: a name that already has a row is left exactly as it is. An operator who turned
    /// an event's sampling down last month must not find it back at 100% because somebody pressed
    /// this.
    /// </para>
    /// </summary>
    [HttpPost("schemas/seed")]
    public async Task<IActionResult> SeedSchemas(CancellationToken cancellationToken)
        => Ok(new { added = await _schemas.SeedAsync(cancellationToken) });

    /// <summary>
    /// Defaults an omitted range to the last thirty days, inclusive of both ends.
    /// <para>
    /// Defaulted here rather than in the service so the service's own guard — which refuses a range
    /// wider than the configured maximum — sees a real range and can say so, instead of silently
    /// interpreting <c>default(DateTime)</c> as the year 1.
    /// </para>
    /// </summary>
    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        var end = (to ?? DateTime.UtcNow).Date;
        var start = (from ?? end.AddDays(-29)).Date;

        return (start, end);
    }
}
