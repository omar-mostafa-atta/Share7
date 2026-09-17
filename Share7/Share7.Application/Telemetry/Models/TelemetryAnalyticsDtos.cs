using Share7.Domain.Telemetry;

namespace Share7.Application.Telemetry.Models;

// ---- overview --------------------------------------------------------------------------------

/// <summary>
/// The headline numbers, for one day range.
/// <para>
/// Everything here reads a rollup. Nothing in this DTO is worth a scan of <c>TelemetryEvents</c>,
/// and the moment one of them is, the answer is a new rollup rather than a slower query — see
/// <c>Docs/AnalyticsArchitecture.md</c> → Rule 4.
/// </para>
/// </summary>
public class AnalyticsOverviewDto
{
    public DateTime FromDayUtc { get; init; }
    public DateTime ToDayUtc { get; init; }

    /// <summary>Distinct users active on the most recent day of the range.</summary>
    public int Dau { get; init; }

    /// <summary>Distinct users active in the seven days ending at <see cref="ToDayUtc"/>.</summary>
    public int Wau { get; init; }

    /// <summary>Distinct users active in the thirty days ending at <see cref="ToDayUtc"/>.</summary>
    public int Mau { get; init; }

    /// <summary>
    /// <c>DAU/MAU</c>. The stickiness ratio — how much of the monthly audience shows up on a given
    /// day. Reported because it is the one engagement number that does not move with acquisition.
    /// </summary>
    public double Stickiness { get; init; }

    /// <summary>Accounts whose cohort day falls in the range.</summary>
    public int NewUsers { get; init; }

    public int Sessions { get; init; }

    public double AverageSessionSeconds { get; init; }

    public double SessionsPerActiveUser { get; init; }

    public long TotalPlaySeconds { get; init; }

    public long TotalEvents { get; init; }

    /// <summary>
    /// Retention headlines, read straight out of <c>TelemetryRetentionCohorts</c>.
    /// <para>
    /// **Each is null until its cohorts have matured.** A D30 figure computed from cohorts that are
    /// eleven days old is not a low number, it is a meaningless one — and rendering it as 0% is how
    /// a team spends a week fixing retention that was never broken.
    /// </para>
    /// </summary>
    public double? D1 { get; init; }
    public double? D7 { get; init; }
    public double? D30 { get; init; }

    /// <summary>How many cohorts each headline was averaged over, so a thin number reads as thin.</summary>
    public int D1CohortCount { get; init; }
    public int D7CohortCount { get; init; }
    public int D30CohortCount { get; init; }

    /// <summary>Distinct users by platform over the range. A dimension breakdown, not a total.</summary>
    public IReadOnlyList<AnalyticsBreakdownDto> Platforms { get; init; } = [];

    /// <summary>How far behind the projector is, in seconds. Zero means fully caught up.</summary>
    public int ProjectionLagSeconds { get; init; }

    /// <summary>Events waiting to be folded. A number that only goes up is a projector that has stopped.</summary>
    public long PendingEvents { get; init; }
}

/// <summary>One slice of a dimension breakdown.</summary>
public class AnalyticsBreakdownDto
{
    public string Key { get; init; } = string.Empty;
    public long Count { get; init; }
    public double Share { get; init; }
}

// ---- retention -------------------------------------------------------------------------------

/// <summary>The cohort triangle: one row per cohort day, one cell per day index.</summary>
public class RetentionReportDto
{
    public DateTime FromCohortDayUtc { get; init; }
    public DateTime ToCohortDayUtc { get; init; }
    public int MaxDayIndex { get; init; }

    public IReadOnlyList<RetentionCohortRowDto> Cohorts { get; init; } = [];

    /// <summary>
    /// Weighted average retention per day index across every cohort in the range — the curve under
    /// the triangle.
    /// <para>
    /// Weighted by cohort size rather than a mean of percentages. An unweighted mean lets a
    /// forty-user Tuesday count as much as a forty-thousand-user launch day, which is how a
    /// retention chart ends up disagreeing with the totals underneath it.
    /// </para>
    /// </summary>
    public IReadOnlyList<RetentionCurvePointDto> Curve { get; init; } = [];

    public DateTime? ComputedAtUtc { get; init; }
}

public class RetentionCohortRowDto
{
    public DateTime CohortDayUtc { get; init; }
    public int CohortSize { get; init; }

    /// <summary>
    /// Indexed by day: <c>Cells[0]</c> is the install day and is always the full cohort. Shorter
    /// than <c>MaxDayIndex</c> for a cohort that has not aged that far — a missing cell means "not
    /// yet known", which is not the same as zero and must not render as it.
    /// </summary>
    public IReadOnlyList<int> Cells { get; init; } = [];
}

public class RetentionCurvePointDto
{
    public int DayIndex { get; init; }
    public double Retention { get; init; }
    public int CohortCount { get; init; }
    public int UserCount { get; init; }
}

// ---- time series -----------------------------------------------------------------------------

/// <summary>One metric over a day range, optionally split by a dimension.</summary>
public class TimeseriesDto
{
    public string Metric { get; init; } = string.Empty;
    public string? Dimension { get; init; }
    public IReadOnlyList<TimeseriesSeriesDto> Series { get; init; } = [];
}

public class TimeseriesSeriesDto
{
    /// <summary>The dimension value, or empty for the ungrouped total.</summary>
    public string Key { get; init; } = string.Empty;
    public IReadOnlyList<TimeseriesPointDto> Points { get; init; } = [];
}

public class TimeseriesPointDto
{
    public DateTime DayUtc { get; init; }
    public long Count { get; init; }

    /// <summary>Null until the nightly pass computes it. Renders as "pending", never as zero.</summary>
    public int? UniqueUsers { get; init; }
}

// ---- event catalogue -------------------------------------------------------------------------

/// <summary>The registry, with what each event has actually been doing.</summary>
public class EventCatalogueDto
{
    public IReadOnlyList<EventCatalogueRowDto> Events { get; init; } = [];

    /// <summary>
    /// Names seen in the wild with no registration. **The queue that keeps the vocabulary honest** —
    /// these are stored but never rolled up until somebody says what they are. See Rule 6.
    /// </summary>
    public IReadOnlyList<EventCatalogueRowDto> Unregistered { get; init; } = [];
}

public class EventCatalogueRowDto
{
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TelemetryCategory Category { get; init; }
    public double SampleRate { get; init; }
    public int? RetentionDays { get; init; }
    public bool Enabled { get; init; }
    public bool RollUpDaily { get; init; }
    public string Dimensions { get; init; } = string.Empty;
    public DateTime? FirstSeenAtUtc { get; init; }

    /// <summary>Occurrences over the queried range. Zero for a registered event nobody emits — which is itself worth seeing.</summary>
    public long Count { get; init; }
}

/// <summary>One event in detail: its volume, and what its parameters actually contain.</summary>
public class EventDetailDto
{
    public EventCatalogueRowDto Schema { get; init; } = new();
    public IReadOnlyList<TimeseriesPointDto> Daily { get; init; } = [];

    /// <summary>
    /// The distinct values each parameter took, most common first, over a **bounded sample** of
    /// recent rows.
    /// <para>
    /// Sampled rather than exact on purpose: an exact breakdown means opening every payload of the
    /// range, and this is a "what does this event look like" question, not a metric. The sample
    /// size is reported so nobody reads it as one.
    /// </para>
    /// </summary>
    public IReadOnlyList<EventParameterDto> Parameters { get; init; } = [];

    public int SampleSize { get; init; }
}

public class EventParameterDto
{
    public string Key { get; init; } = string.Empty;
    public IReadOnlyList<AnalyticsBreakdownDto> TopValues { get; init; } = [];
    public int DistinctValues { get; init; }
}

// ---- funnels ---------------------------------------------------------------------------------

/// <summary>
/// Ordered steps and how many users reached each, within a window of their first step.
/// <para>
/// **Per user, not per event.** A funnel counted on occurrences lets one child who opened the shop
/// forty times look like forty children, which is how a conversion rate ends up above 100%.
/// </para>
/// </summary>
public class FunnelReportDto
{
    public IReadOnlyList<FunnelStepDto> Steps { get; init; } = [];
    public int WindowHours { get; init; }
    public DateTime FromDayUtc { get; init; }
    public DateTime ToDayUtc { get; init; }
}

public class FunnelStepDto
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Users { get; init; }

    /// <summary>Share of the users who reached step 0. The number people mean by "conversion".</summary>
    public double ConversionFromStart { get; init; }

    /// <summary>Share of the users who reached the previous step. The number that shows where the drop is.</summary>
    public double ConversionFromPrevious { get; init; }
}

// ---- economy ---------------------------------------------------------------------------------

/// <summary>
/// Where virtual currency comes from and where it goes, per day.
/// <para>
/// **Read from <c>CurrencyLedgerEntries</c>, not from telemetry.** The ledger is the authoritative
/// record of every grant and spend; a second count assembled from client events would eventually
/// disagree with it, and the ledger would be right. See Rule 2.
/// </para>
/// </summary>
public class EconomyReportDto
{
    public DateTime FromDayUtc { get; init; }
    public DateTime ToDayUtc { get; init; }
    public IReadOnlyList<EconomyCurrencyDto> Currencies { get; init; } = [];
}

public class EconomyCurrencyDto
{
    public Guid CurrencyId { get; init; }
    public string Code { get; init; } = string.Empty;

    /// <summary>Total credited over the range.</summary>
    public long Sourced { get; init; }

    /// <summary>Total debited over the range, as a positive number.</summary>
    public long Sunk { get; init; }

    /// <summary>
    /// <c>Sourced - Sunk</c>. Sustained positive net is inflation: the economy is minting faster
    /// than it removes, and every price in the shop is quietly getting cheaper.
    /// </summary>
    public long Net { get; init; }

    /// <summary>Where it came from, by ledger source type.</summary>
    public IReadOnlyList<AnalyticsBreakdownDto> Sources { get; init; } = [];

    /// <summary>Where it went.</summary>
    public IReadOnlyList<AnalyticsBreakdownDto> Sinks { get; init; } = [];

    public IReadOnlyList<EconomyDailyPointDto> Daily { get; init; } = [];
}

public class EconomyDailyPointDto
{
    public DateTime DayUtc { get; init; }
    public long Sourced { get; init; }
    public long Sunk { get; init; }
}

// ---- registry writes -------------------------------------------------------------------------

/// <summary>Registers a name, or re-authors one. Same shape both ways — the name is the key.</summary>
public class UpsertEventSchemaRequest
{
    public string Group { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TelemetryCategory Category { get; set; } = TelemetryCategory.Behavioural;
    public double SampleRate { get; set; } = 1.0;
    public int? RetentionDays { get; set; }
    public bool Enabled { get; set; } = true;
    public bool RollUpDaily { get; set; } = true;

    /// <summary>Comma-separated, from <c>TelemetryDimensions.All</c>. Empty for the ungrouped total only.</summary>
    public string Dimensions { get; set; } = string.Empty;
}
