using Share7.Application.Common.Models;
using Share7.Application.Telemetry.Models;

namespace Share7.Application.Telemetry.Interfaces;

/// <summary>
/// Accepts a batch of client events.
/// <para>
/// **The hottest write path in the platform, and the only thing it may do is one insert.** Every
/// derived number — DAU, retention, funnels — is somebody else's job, computed later by the
/// projector. An ingest that also updated a counter would put a contended row in front of a
/// million concurrent writers, and the whole rollup design exists so it does not have to.
/// </para>
/// </summary>
public interface ITelemetryIngestService
{
    /// <summary>
    /// Validates, scrubs, de-duplicates and stores one batch.
    /// <para>
    /// <paramref name="userId"/> comes from the authenticated request, never from the body — see
    /// <c>TelemetryBatchRequest</c> for why that absence is a guarantee rather than an omission.
    /// </para>
    /// <para>
    /// A batch is never all-or-nothing: valid events are stored even when others alongside them are
    /// refused. Failing the batch would make one malformed event on a shipped build cost every
    /// other event that device ever queues behind it.
    /// </para>
    /// </summary>
    Task<ServiceResult<TelemetryBatchResponse>> IngestAsync(
        Guid userId,
        TelemetryBatchRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Folds raw events into the rollups, and rebuilds the derived tables.
/// <para>
/// Every rule lives here rather than in the <c>BackgroundService</c> that ticks it, so a test can
/// run one pass and assert on it — the same split <c>GameResultRetentionSweeper</c> uses.
/// </para>
/// </summary>
public interface ITelemetryRollupService
{
    /// <summary>
    /// Folds one batch of pending events and advances the watermark **in the same transaction**.
    /// <para>
    /// Reads only rows older than the safety lag: <c>Sequence</c> is an identity column, so a
    /// higher value can commit before a lower one, and a projector that watermarked at
    /// <c>MAX(Sequence)</c> would skip the straggler permanently.
    /// </para>
    /// </summary>
    /// <returns>How many events were folded. Fewer than the batch size means the backlog is drained.</returns>
    Task<int> ProjectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rebuilds <c>TelemetryRetentionCohorts</c> and fills in unique-user counts, for the recent
    /// window only.
    /// <para>
    /// Recent-window rather than everything, because a batch arriving late can add activity to a
    /// day already summarised. Older cohorts are settled and rewriting them nightly would be work
    /// that changes nothing.
    /// </para>
    /// </summary>
    Task<int> RunNightlyAsync(CancellationToken cancellationToken);
}

/// <summary>Deletes raw events past their retention. **Rollups are never swept.**</summary>
public interface ITelemetryRetentionService
{
    /// <summary>One bounded delete pass. Returns rows removed; fewer than the batch size means done.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Everything the console reads. All of it comes from rollups and ledgers — nothing here scans
/// <c>TelemetryEvents</c> except the two reads that are explicitly bounded and documented as such.
/// </summary>
public interface ITelemetryAnalyticsService
{
    Task<ServiceResult<AnalyticsOverviewDto>> GetOverviewAsync(
        DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken);

    Task<ServiceResult<RetentionReportDto>> GetRetentionAsync(
        DateTime fromCohortDayUtc, DateTime toCohortDayUtc, int maxDayIndex, CancellationToken cancellationToken);

    Task<ServiceResult<TimeseriesDto>> GetTimeseriesAsync(
        string metric, DateTime fromDayUtc, DateTime toDayUtc, string? dimension, CancellationToken cancellationToken);

    Task<ServiceResult<EventCatalogueDto>> GetEventCatalogueAsync(
        DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken);

    Task<ServiceResult<EventDetailDto>> GetEventDetailAsync(
        string name, DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Conversion through ordered steps, counted per user within a window of their first step.
    /// </summary>
    Task<ServiceResult<FunnelReportDto>> GetFunnelAsync(
        IReadOnlyList<string> steps, DateTime fromDayUtc, DateTime toDayUtc, int windowHours,
        CancellationToken cancellationToken);

    /// <summary>Currency in and out, **from the ledger** — see <c>EconomyReportDto</c>.</summary>
    Task<ServiceResult<EconomyReportDto>> GetEconomyAsync(
        DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken);
}

/// <summary>The event-name registry. Authored data, edited by an operator.</summary>
public interface ITelemetrySchemaService
{
    Task<ServiceResult<EventCatalogueRowDto>> UpsertAsync(
        string name, UpsertEventSchemaRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Registers every name in <c>TelemetryNames</c> that has no row yet, at its documented
    /// defaults. Idempotent, and run at startup so a fresh database has a vocabulary before its
    /// first client connects.
    /// </summary>
    Task<int> SeedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The per-account trace: everything that happened to one child, from every table that recorded
/// any of it, in one order.
/// <para>
/// **A read across the authoritative tables, never a copy of them.** See
/// <c>TimelineSourceKind</c>.
/// </para>
/// </summary>
public interface IUserTimelineService
{
    Task<ServiceResult<UserAnalyticsProfileDto>> GetProfileAsync(
        Guid userId, CancellationToken cancellationToken);

    /// <param name="beforeUtc">Cursor — return entries strictly older than this. Null starts at the newest.</param>
    /// <param name="sources">Restrict to these sources, or null for all of them.</param>
    Task<ServiceResult<UserTimelineDto>> GetTimelineAsync(
        Guid userId,
        DateTime? beforeUtc,
        int limit,
        IReadOnlyList<TimelineSourceKind>? sources,
        CancellationToken cancellationToken);
}
