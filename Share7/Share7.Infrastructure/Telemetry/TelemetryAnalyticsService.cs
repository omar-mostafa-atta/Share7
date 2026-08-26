using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Telemetry;

/// <summary>
/// Everything the console reads.
/// <para>
/// **Nothing here scans <c>TelemetryEvents</c> except where it is explicitly bounded and said so.**
/// Overview, retention, trends and the economy report all read rollups or ledgers; the two that do
/// open raw rows — the parameter breakdown and the funnel — are capped and documented at the point
/// they do it. The moment a new dashboard number wants a scan, the answer is a new rollup, not a
/// slower query. See <c>Docs/AnalyticsArchitecture.md</c> → Rule 4.
/// </para>
/// </summary>
public class TelemetryAnalyticsService : ITelemetryAnalyticsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TelemetryOptions _options;

    public TelemetryAnalyticsService(ApplicationDbContext dbContext, IOptions<TelemetryOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    // ---- overview -------------------------------------------------------------------------------

    public async Task<ServiceResult<AnalyticsOverviewDto>> GetOverviewAsync(
        DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken)
    {
        if (!TryNormaliseRange(ref fromDayUtc, ref toDayUtc, out var rangeError))
            return ServiceResult<AnalyticsOverviewDto>.Invalid(rangeError!);

        var wauFrom = toDayUtc.AddDays(-6);
        var mauFrom = toDayUtc.AddDays(-29);

        // One pass over the widest window any of the three needs, then counted in memory. Three
        // separate COUNT(DISTINCT) queries would each re-read overlapping slices of the same rows.
        var activity = await _dbContext.TelemetryUserDays
            .AsNoTracking()
            .Where(d => d.DayUtc >= mauFrom && d.DayUtc <= toDayUtc)
            .Select(d => new { d.UserId, d.DayUtc, d.PlaySeconds, d.EventCount })
            .ToListAsync(cancellationToken);

        var dau = activity.Where(a => a.DayUtc == toDayUtc).Select(a => a.UserId).Distinct().Count();
        var wau = activity.Where(a => a.DayUtc >= wauFrom).Select(a => a.UserId).Distinct().Count();
        var mau = activity.Select(a => a.UserId).Distinct().Count();

        var inRange = activity.Where(a => a.DayUtc >= fromDayUtc).ToList();

        var newUsers = await _dbContext.TelemetryUserLifecycle
            .AsNoTracking()
            .CountAsync(l => l.CohortDayUtc >= fromDayUtc && l.CohortDayUtc <= toDayUtc, cancellationToken);

        var sessionStats = await _dbContext.TelemetrySessions
            .AsNoTracking()
            .Where(s => s.DayUtc >= fromDayUtc && s.DayUtc <= toDayUtc)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),

                // Averaged over closed sessions only. A session with no end never had its length
                // measured, and counting it as zero would drag the average toward the crash rate
                // rather than reporting how long children actually play.
                Closed = g.Count(s => s.EndedAtUtc != null),
                TotalSeconds = g.Sum(s => (long)s.DurationSeconds)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var sessions = sessionStats?.Count ?? 0;
        var closedSessions = sessionStats?.Closed ?? 0;
        var totalSessionSeconds = sessionStats?.TotalSeconds ?? 0;

        var activeInRange = inRange.Select(a => a.UserId).Distinct().Count();

        var platforms = await _dbContext.TelemetrySessions
            .AsNoTracking()
            .Where(s => s.DayUtc >= fromDayUtc && s.DayUtc <= toDayUtc)
            .GroupBy(s => s.Platform)
            .Select(g => new { Platform = g.Key, Users = g.Select(s => s.UserId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        var platformTotal = platforms.Sum(p => (long)p.Users);

        var (d1, d1Count) = await HeadlineRetentionAsync(1, fromDayUtc, toDayUtc, cancellationToken);
        var (d7, d7Count) = await HeadlineRetentionAsync(7, fromDayUtc, toDayUtc, cancellationToken);
        var (d30, d30Count) = await HeadlineRetentionAsync(30, fromDayUtc, toDayUtc, cancellationToken);

        var (lag, pending) = await ProjectionHealthAsync(cancellationToken);

        return ServiceResult<AnalyticsOverviewDto>.Success(new AnalyticsOverviewDto
        {
            FromDayUtc = fromDayUtc,
            ToDayUtc = toDayUtc,
            Dau = dau,
            Wau = wau,
            Mau = mau,
            Stickiness = mau > 0 ? Math.Round((double)dau / mau, 4) : 0,
            NewUsers = newUsers,
            Sessions = sessions,
            AverageSessionSeconds = closedSessions > 0
                ? Math.Round((double)totalSessionSeconds / closedSessions, 1)
                : 0,
            SessionsPerActiveUser = activeInRange > 0
                ? Math.Round((double)sessions / activeInRange, 2)
                : 0,
            TotalPlaySeconds = inRange.Sum(a => (long)a.PlaySeconds),
            TotalEvents = inRange.Sum(a => (long)a.EventCount),
            D1 = d1,
            D7 = d7,
            D30 = d30,
            D1CohortCount = d1Count,
            D7CohortCount = d7Count,
            D30CohortCount = d30Count,
            Platforms = platforms
                .OrderByDescending(p => p.Users)
                .Select(p => new AnalyticsBreakdownDto
                {
                    Key = string.IsNullOrEmpty(p.Platform) ? "unknown" : p.Platform,
                    Count = p.Users,
                    Share = platformTotal > 0 ? Math.Round((double)p.Users / platformTotal, 4) : 0
                })
                .ToList(),
            ProjectionLagSeconds = lag,
            PendingEvents = pending
        });
    }

    /// <summary>
    /// The D-N headline, averaged over the cohorts in range that have actually matured.
    /// <para>
    /// **Null when no cohort is old enough, and never zero.** A D30 computed from cohorts eleven
    /// days old is not a low number, it is a meaningless one — and rendering it as 0% is how a team
    /// spends a week fixing retention that was never broken.
    /// </para>
    /// <para>
    /// Weighted by cohort size rather than a mean of percentages: an unweighted mean lets a
    /// forty-user Tuesday count for as much as a forty-thousand-user launch day.
    /// </para>
    /// </summary>
    private async Task<(double? Value, int Cohorts)> HeadlineRetentionAsync(
        int dayIndex, DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken)
    {
        var matured = DateTime.UtcNow.Date.AddDays(-dayIndex);
        var ceiling = toDayUtc < matured ? toDayUtc : matured;

        if (ceiling < fromDayUtc) return (null, 0);

        var cells = await _dbContext.TelemetryRetentionCohorts
            .AsNoTracking()
            .Where(c => c.DayIndex == dayIndex && c.CohortDayUtc >= fromDayUtc && c.CohortDayUtc <= ceiling)
            .Select(c => new { c.CohortSize, c.RetainedUsers })
            .ToListAsync(cancellationToken);

        if (cells.Count == 0) return (null, 0);

        var size = cells.Sum(c => (long)c.CohortSize);
        if (size == 0) return (null, cells.Count);

        return (Math.Round((double)cells.Sum(c => (long)c.RetainedUsers) / size, 4), cells.Count);
    }

    /// <summary>
    /// How far behind the projector is.
    /// <para>
    /// On the overview because a stalled projector looks exactly like a collapse in engagement —
    /// every number goes flat — and the only thing that tells them apart is this pair.
    /// </para>
    /// </summary>
    private async Task<(int LagSeconds, long Pending)> ProjectionHealthAsync(CancellationToken cancellationToken)
    {
        var watermark = await _dbContext.ProjectionCheckpoints
            .AsNoTracking()
            .Where(c => c.Consumer == ProjectionConsumers.Telemetry)
            .Select(c => (long?)c.Watermark)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var oldest = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.Sequence > watermark)
            .OrderBy(e => e.Sequence)
            .Select(e => (DateTime?)e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (oldest is null) return (0, 0);

        var pending = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .LongCountAsync(e => e.Sequence > watermark, cancellationToken);

        return ((int)Math.Max(0, (DateTime.UtcNow - oldest.Value).TotalSeconds), pending);
    }

    // ---- retention ------------------------------------------------------------------------------

    public async Task<ServiceResult<RetentionReportDto>> GetRetentionAsync(
        DateTime fromCohortDayUtc, DateTime toCohortDayUtc, int maxDayIndex, CancellationToken cancellationToken)
    {
        if (!TryNormaliseRange(ref fromCohortDayUtc, ref toCohortDayUtc, out var rangeError))
            return ServiceResult<RetentionReportDto>.Invalid(rangeError!);

        maxDayIndex = Math.Clamp(maxDayIndex <= 0 ? 30 : maxDayIndex, 1, _options.MaxCohortDayIndex);

        var cells = await _dbContext.TelemetryRetentionCohorts
            .AsNoTracking()
            .Where(c => c.CohortDayUtc >= fromCohortDayUtc &&
                        c.CohortDayUtc <= toCohortDayUtc &&
                        c.DayIndex <= maxDayIndex)
            .OrderBy(c => c.CohortDayUtc).ThenBy(c => c.DayIndex)
            .ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;
        var rows = new List<RetentionCohortRowDto>();

        foreach (var group in cells.GroupBy(c => c.CohortDayUtc).OrderByDescending(g => g.Key))
        {
            var size = group.Max(c => c.CohortSize);

            // Only as far as the cohort has actually aged. A cell beyond that is *unknown*, not
            // zero, and a triangle that renders unknown as zero shows a cliff that is really just
            // the present day.
            var observable = Math.Min(maxDayIndex, (int)(today - group.Key).TotalDays);
            if (observable < 0) continue;

            var cellsForRow = new int[observable + 1];

            foreach (var cell in group)
            {
                if (cell.DayIndex <= observable) cellsForRow[cell.DayIndex] = cell.RetainedUsers;
            }

            rows.Add(new RetentionCohortRowDto
            {
                CohortDayUtc = group.Key,
                CohortSize = size,
                Cells = cellsForRow
            });
        }

        var curve = cells
            .GroupBy(c => c.DayIndex)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var size = g.Sum(c => (long)c.CohortSize);
                var retained = g.Sum(c => (long)c.RetainedUsers);

                return new RetentionCurvePointDto
                {
                    DayIndex = g.Key,
                    Retention = size > 0 ? Math.Round((double)retained / size, 4) : 0,
                    CohortCount = g.Count(),
                    UserCount = (int)retained
                };
            })
            .ToList();

        return ServiceResult<RetentionReportDto>.Success(new RetentionReportDto
        {
            FromCohortDayUtc = fromCohortDayUtc,
            ToCohortDayUtc = toCohortDayUtc,
            MaxDayIndex = maxDayIndex,
            Cohorts = rows,
            Curve = curve,
            ComputedAtUtc = cells.Count > 0 ? cells.Max(c => c.ComputedAtUtc) : null
        });
    }

    // ---- time series ----------------------------------------------------------------------------

    public async Task<ServiceResult<TimeseriesDto>> GetTimeseriesAsync(
        string metric, DateTime fromDayUtc, DateTime toDayUtc, string? dimension,
        CancellationToken cancellationToken)
    {
        if (!TryNormaliseRange(ref fromDayUtc, ref toDayUtc, out var rangeError))
            return ServiceResult<TimeseriesDto>.Invalid(rangeError!);

        if (string.IsNullOrWhiteSpace(metric))
            return ServiceResult<TimeseriesDto>.Invalid("A metric name is required.");

        // Two synthetic metrics that are not event counts at all. They read TelemetryUserDays,
        // which is the only place a distinct-user-per-day number exists — and they are named here
        // rather than left to the caller so the console has one route for every chart.
        if (metric is "active_users" or "new_users")
            return await UserSeriesAsync(metric, fromDayUtc, toDayUtc, cancellationToken);

        var dim = string.IsNullOrWhiteSpace(dimension) ? string.Empty : dimension.Trim();

        if (dim.Length > 0 && !TelemetryDimensions.All.Contains(dim))
            return ServiceResult<TimeseriesDto>.Invalid($"Unknown dimension '{dim}'.");

        var rows = await _dbContext.TelemetryDailyMetrics
            .AsNoTracking()
            .Where(m => m.Name == metric && m.DayUtc >= fromDayUtc && m.DayUtc <= toDayUtc && m.Dimension == dim)
            .OrderBy(m => m.DayUtc)
            .ToListAsync(cancellationToken);

        var series = rows
            .GroupBy(m => m.DimensionValue)
            .Select(g => new TimeseriesSeriesDto
            {
                Key = g.Key,
                Points = g.Select(m => new TimeseriesPointDto
                {
                    DayUtc = m.DayUtc,
                    Count = m.Count,
                    UniqueUsers = m.UniqueUsers
                }).ToList()
            })
            .OrderByDescending(s => s.Points.Sum(p => p.Count))
            .ToList();

        return ServiceResult<TimeseriesDto>.Success(new TimeseriesDto
        {
            Metric = metric,
            Dimension = dim.Length > 0 ? dim : null,
            Series = series
        });
    }

    private async Task<ServiceResult<TimeseriesDto>> UserSeriesAsync(
        string metric, DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken)
    {
        List<TimeseriesPointDto> points;

        if (metric == "new_users")
        {
            points = await _dbContext.TelemetryUserLifecycle
                .AsNoTracking()
                .Where(l => l.CohortDayUtc >= fromDayUtc && l.CohortDayUtc <= toDayUtc)
                .GroupBy(l => l.CohortDayUtc)
                .OrderBy(g => g.Key)
                .Select(g => new TimeseriesPointDto
                {
                    DayUtc = g.Key,
                    Count = g.Count(),
                    UniqueUsers = g.Count()
                })
                .ToListAsync(cancellationToken);
        }
        else
        {
            // One row per user per day already, so a plain Count() is the distinct count. This is
            // exactly what TelemetryUserDays exists for.
            points = await _dbContext.TelemetryUserDays
                .AsNoTracking()
                .Where(d => d.DayUtc >= fromDayUtc && d.DayUtc <= toDayUtc)
                .GroupBy(d => d.DayUtc)
                .OrderBy(g => g.Key)
                .Select(g => new TimeseriesPointDto
                {
                    DayUtc = g.Key,
                    Count = g.Count(),
                    UniqueUsers = g.Count()
                })
                .ToListAsync(cancellationToken);
        }

        return ServiceResult<TimeseriesDto>.Success(new TimeseriesDto
        {
            Metric = metric,
            Series = [new TimeseriesSeriesDto { Key = string.Empty, Points = points }]
        });
    }

    // ---- event catalogue ------------------------------------------------------------------------

    public async Task<ServiceResult<EventCatalogueDto>> GetEventCatalogueAsync(
        DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken)
    {
        if (!TryNormaliseRange(ref fromDayUtc, ref toDayUtc, out var rangeError))
            return ServiceResult<EventCatalogueDto>.Invalid(rangeError!);

        var schemas = await _dbContext.TelemetryEventSchemas
            .AsNoTracking()
            .OrderBy(s => s.Group).ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var volumes = await _dbContext.TelemetryDailyMetrics
            .AsNoTracking()
            .Where(m => m.DayUtc >= fromDayUtc && m.DayUtc <= toDayUtc && m.Dimension == string.Empty)
            .GroupBy(m => m.Name)
            .Select(g => new { Name = g.Key, Total = g.Sum(m => m.Count) })
            .ToDictionaryAsync(x => x.Name, x => x.Total, StringComparer.Ordinal, cancellationToken);

        // Unregistered names have no rollup by design, so their volume has to come from raw. Bounded
        // by the day index and by there being at most a handful of them — the ingest cap is what
        // makes that true rather than hoped.
        var unregisteredNames = schemas
            .Where(s => s.FirstSeenAtUtc is not null)
            .Select(s => s.Name)
            .ToList();

        var unregisteredVolumes = new Dictionary<string, long>(StringComparer.Ordinal);

        if (unregisteredNames.Count > 0)
        {
            unregisteredVolumes = await _dbContext.TelemetryEvents
                .AsNoTracking()
                .Where(e => e.DayUtc >= fromDayUtc && e.DayUtc <= toDayUtc && unregisteredNames.Contains(e.Name))
                .GroupBy(e => e.Name)
                .Select(g => new { Name = g.Key, Total = g.LongCount() })
                .ToDictionaryAsync(x => x.Name, x => x.Total, StringComparer.Ordinal, cancellationToken);
        }

        EventCatalogueRowDto Row(TelemetryEventSchema s) => new()
        {
            Name = s.Name,
            Group = s.Group,
            Description = s.Description,
            Category = s.Category,
            SampleRate = s.SampleRate,
            RetentionDays = s.RetentionDays,
            Enabled = s.Enabled,
            RollUpDaily = s.RollUpDaily,
            Dimensions = s.Dimensions,
            FirstSeenAtUtc = s.FirstSeenAtUtc,
            Count = volumes.TryGetValue(s.Name, out var v)
                ? v
                : unregisteredVolumes.TryGetValue(s.Name, out var u) ? u : 0
        };

        return ServiceResult<EventCatalogueDto>.Success(new EventCatalogueDto
        {
            Events = schemas.Where(s => s.FirstSeenAtUtc is null).Select(Row).ToList(),
            Unregistered = schemas.Where(s => s.FirstSeenAtUtc is not null).Select(Row).ToList()
        });
    }

    public async Task<ServiceResult<EventDetailDto>> GetEventDetailAsync(
        string name, DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken)
    {
        if (!TryNormaliseRange(ref fromDayUtc, ref toDayUtc, out var rangeError))
            return ServiceResult<EventDetailDto>.Invalid(rangeError!);

        var schema = await _dbContext.TelemetryEventSchemas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);

        if (schema is null)
            return ServiceResult<EventDetailDto>.NotFound($"No event named '{name}'.");

        var daily = await _dbContext.TelemetryDailyMetrics
            .AsNoTracking()
            .Where(m => m.Name == name && m.DayUtc >= fromDayUtc && m.DayUtc <= toDayUtc && m.Dimension == string.Empty)
            .OrderBy(m => m.DayUtc)
            .Select(m => new TimeseriesPointDto
            {
                DayUtc = m.DayUtc,
                Count = m.Count,
                UniqueUsers = m.UniqueUsers
            })
            .ToListAsync(cancellationToken);

        // **The one deliberate read of raw payloads, and it is capped.** "What does this event
        // actually contain" is a shape question, not a metric — an exact breakdown would mean
        // opening every payload in the range, which at scale is the scan this whole design avoids.
        // The sample size is returned so nobody reads the result as a total.
        const int sampleCap = 2000;

        var sample = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.Name == name && e.DayUtc >= fromDayUtc && e.DayUtc <= toDayUtc)
            .OrderByDescending(e => e.Sequence)
            .Take(sampleCap)
            .Select(e => e.ParamsJson)
            .ToListAsync(cancellationToken);

        var buckets = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

        foreach (var json in sample)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") continue;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!buckets.TryGetValue(property.Name, out var values))
                    {
                        values = new Dictionary<string, long>(StringComparer.Ordinal);
                        buckets[property.Name] = values;
                    }

                    var text = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        _ => property.Value.GetRawText()
                    };

                    // Truncated before it becomes a key. A high-cardinality parameter would
                    // otherwise build a dictionary the size of the sample, which is the wrong shape
                    // for a breakdown and the wrong amount of memory for a console page.
                    if (text.Length > 48) text = text[..48];

                    values[text] = values.GetValueOrDefault(text) + 1;
                }
            }
            catch (JsonException)
            {
                // One malformed payload from an old build must not fail the page for the rest.
            }
        }

        var parameters = buckets
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .Select(b =>
            {
                var total = b.Value.Values.Sum();

                return new EventParameterDto
                {
                    Key = b.Key,
                    DistinctValues = b.Value.Count,
                    TopValues = b.Value
                        .OrderByDescending(v => v.Value)
                        .Take(10)
                        .Select(v => new AnalyticsBreakdownDto
                        {
                            Key = v.Key,
                            Count = v.Value,
                            Share = total > 0 ? Math.Round((double)v.Value / total, 4) : 0
                        })
                        .ToList()
                };
            })
            .ToList();

        return ServiceResult<EventDetailDto>.Success(new EventDetailDto
        {
            Schema = new EventCatalogueRowDto
            {
                Name = schema.Name,
                Group = schema.Group,
                Description = schema.Description,
                Category = schema.Category,
                SampleRate = schema.SampleRate,
                RetentionDays = schema.RetentionDays,
                Enabled = schema.Enabled,
                RollUpDaily = schema.RollUpDaily,
                Dimensions = schema.Dimensions,
                FirstSeenAtUtc = schema.FirstSeenAtUtc,
                Count = daily.Sum(d => d.Count)
            },
            Daily = daily,
            Parameters = parameters,
            SampleSize = sample.Count
        });
    }

    // ---- funnels --------------------------------------------------------------------------------

    public async Task<ServiceResult<FunnelReportDto>> GetFunnelAsync(
        IReadOnlyList<string> steps, DateTime fromDayUtc, DateTime toDayUtc, int windowHours,
        CancellationToken cancellationToken)
    {
        if (!TryNormaliseRange(ref fromDayUtc, ref toDayUtc, out var rangeError))
            return ServiceResult<FunnelReportDto>.Invalid(rangeError!);

        if (steps.Count is < 2 or > 8)
            return ServiceResult<FunnelReportDto>.Invalid("A funnel needs between two and eight steps.");

        windowHours = Math.Clamp(windowHours <= 0 ? 24 : windowHours, 1, 24 * 30);

        // **The second deliberate raw read.** A funnel is inherently per-user and ordered, so no
        // daily rollup can answer it — the counters know how many times each step happened, not
        // whether the same child did them in order. Bounded to the step names and the day range,
        // which the (DayUtc, Name) index serves directly.
        var names = steps.Distinct(StringComparer.Ordinal).ToList();

        var rows = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DayUtc >= fromDayUtc && e.DayUtc <= toDayUtc && names.Contains(e.Name))
            .Select(e => new { e.UserId, e.Name, e.OccurredAtUtc })
            .ToListAsync(cancellationToken);

        var window = TimeSpan.FromHours(windowHours);
        var reached = new int[steps.Count];

        foreach (var byUser in rows.GroupBy(r => r.UserId))
        {
            var ordered = byUser.OrderBy(r => r.OccurredAtUtc).ToList();

            // The first occurrence of step 0 starts this user's window. Anything before it is a
            // previous visit, and counting it would let a child who did step 3 last week appear to
            // have converted from a step 0 they did today.
            var start = ordered.FirstOrDefault(r => r.Name == steps[0]);
            if (start is null) continue;

            reached[0]++;

            var cursor = start.OccurredAtUtc;
            var deadline = cursor + window;

            for (var i = 1; i < steps.Count; i++)
            {
                // Strictly after the previous step and inside the window: a funnel counted without
                // ordering is just an intersection of populations, which always looks healthier
                // than the journey actually is.
                var hit = ordered.FirstOrDefault(r =>
                    r.Name == steps[i] && r.OccurredAtUtc >= cursor && r.OccurredAtUtc <= deadline);

                if (hit is null) break;

                reached[i]++;
                cursor = hit.OccurredAtUtc;
            }
        }

        var stepDtos = new List<FunnelStepDto>(steps.Count);

        for (var i = 0; i < steps.Count; i++)
        {
            stepDtos.Add(new FunnelStepDto
            {
                Index = i,
                Name = steps[i],
                Users = reached[i],
                ConversionFromStart = reached[0] > 0 ? Math.Round((double)reached[i] / reached[0], 4) : 0,
                ConversionFromPrevious = i == 0
                    ? 1
                    : reached[i - 1] > 0 ? Math.Round((double)reached[i] / reached[i - 1], 4) : 0
            });
        }

        return ServiceResult<FunnelReportDto>.Success(new FunnelReportDto
        {
            Steps = stepDtos,
            WindowHours = windowHours,
            FromDayUtc = fromDayUtc,
            ToDayUtc = toDayUtc
        });
    }

    // ---- economy --------------------------------------------------------------------------------

    /// <summary>
    /// **Read from <c>CurrencyLedgerEntries</c>, and never from telemetry.** The ledger is the
    /// authoritative record of every credit and debit; a second count assembled from client events
    /// would eventually disagree with it, and the ledger would be the one that is right. See Rule 2.
    /// </summary>
    public async Task<ServiceResult<EconomyReportDto>> GetEconomyAsync(
        DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken)
    {
        if (!TryNormaliseRange(ref fromDayUtc, ref toDayUtc, out var rangeError))
            return ServiceResult<EconomyReportDto>.Invalid(rangeError!);

        var to = toDayUtc.AddDays(1);

        var entries = await _dbContext.CurrencyLedgerEntries
            .AsNoTracking()
            .Where(e => e.CreatedAtUtc >= fromDayUtc && e.CreatedAtUtc < to)
            .Select(e => new
            {
                e.CurrencyId,
                e.Amount,
                e.SourceType,
                e.TransactionType,
                e.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var currencies = await _dbContext.Currencies
            .AsNoTracking()
            .Select(c => new { c.Id, c.Key })
            .ToDictionaryAsync(c => c.Id, c => c.Key, cancellationToken);

        var report = entries
            .GroupBy(e => e.CurrencyId)
            .Select(g =>
            {
                var credits = g.Where(e => e.Amount > 0).ToList();
                var debits = g.Where(e => e.Amount < 0).ToList();

                var sourced = credits.Sum(e => e.Amount);
                var sunk = -debits.Sum(e => e.Amount);

                return new EconomyCurrencyDto
                {
                    CurrencyId = g.Key,
                    Code = currencies.TryGetValue(g.Key, out var key) ? key : "unknown",
                    Sourced = sourced,
                    Sunk = sunk,
                    Net = sourced - sunk,
                    Sources = Breakdown(credits.Select(e => (e.TransactionType.ToString(), e.Amount)), sourced),
                    Sinks = Breakdown(debits.Select(e => (e.TransactionType.ToString(), -e.Amount)), sunk),
                    Daily = g
                        .GroupBy(e => e.CreatedAtUtc.Date)
                        .OrderBy(d => d.Key)
                        .Select(d => new EconomyDailyPointDto
                        {
                            DayUtc = d.Key,
                            Sourced = d.Where(e => e.Amount > 0).Sum(e => e.Amount),
                            Sunk = -d.Where(e => e.Amount < 0).Sum(e => e.Amount)
                        })
                        .ToList()
                };
            })
            .OrderByDescending(c => c.Sourced)
            .ToList();

        return ServiceResult<EconomyReportDto>.Success(new EconomyReportDto
        {
            FromDayUtc = fromDayUtc,
            ToDayUtc = toDayUtc,
            Currencies = report
        });
    }

    private static List<AnalyticsBreakdownDto> Breakdown(
        IEnumerable<(string Key, long Amount)> rows, long total) =>
        rows
            .GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => new AnalyticsBreakdownDto
            {
                Key = g.Key,
                Count = g.Sum(r => r.Amount),
                Share = total > 0 ? Math.Round((double)g.Sum(r => r.Amount) / total, 4) : 0
            })
            .OrderByDescending(b => b.Count)
            .ToList();

    // ---- shared ---------------------------------------------------------------------------------

    /// <summary>
    /// Normalises a day range and refuses one that is too wide.
    /// <para>
    /// The cap is not defensive tidying: at scale a three-year range typed by accident is an outage
    /// rather than a slow page, and the console has no reason to ask for one.
    /// </para>
    /// </summary>
    private bool TryNormaliseRange(ref DateTime from, ref DateTime to, out string? error)
    {
        error = null;

        from = from.Date;
        to = to == default ? DateTime.UtcNow.Date : to.Date;

        if (from == default) from = to.AddDays(-29);

        if (from > to)
        {
            (from, to) = (to, from);
        }

        if ((to - from).TotalDays > _options.MaxQueryRangeDays)
        {
            error = $"A range may cover at most {_options.MaxQueryRangeDays} days.";
            return false;
        }

        return true;
    }
}
