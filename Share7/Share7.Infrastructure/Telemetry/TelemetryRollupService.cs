using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Telemetry;

/// <summary>
/// Folds the raw event stream into the tables every dashboard actually reads.
/// <para>
/// Same shape as <c>ObjectiveProjector</c>: read by <c>Sequence</c> above a watermark, fold,
/// advance the watermark in the same <c>SaveChanges</c>. Consumer name <c>telemetry</c>, which is
/// exactly the generality <c>ProjectionCheckpoint</c> was built for — a second stream with its own
/// cursor, independent of the leaderboard projector's.
/// </para>
/// <para>
/// **Two hazards, both handled here rather than hoped away.** The safety lag closes the identity
/// gap (a higher <c>Sequence</c> can commit before a lower one, and a projector that watermarked
/// at the maximum would skip the straggler for good). The per-row <c>LastSequence</c> guards close
/// double-counting when two app instances project the same batch at once — which on IIS with more
/// than one worker is not a hypothetical.
/// </para>
/// </summary>
public class TelemetryRollupService : ITelemetryRollupService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TelemetryOptions _options;
    private readonly ILogger<TelemetryRollupService> _logger;

    public TelemetryRollupService(
        ApplicationDbContext dbContext,
        IOptions<TelemetryOptions> options,
        ILogger<TelemetryRollupService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    // ---- the streaming fold ---------------------------------------------------------------------

    public async Task<int> ProjectAsync(CancellationToken cancellationToken)
    {
        var checkpoint = await _dbContext.ProjectionCheckpoints
            .FirstOrDefaultAsync(c => c.Consumer == ProjectionConsumers.Telemetry, cancellationToken);

        if (checkpoint is null)
        {
            checkpoint = new ProjectionCheckpoint
            {
                Consumer = ProjectionConsumers.Telemetry,
                Watermark = 0,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _dbContext.ProjectionCheckpoints.Add(checkpoint);
        }

        // **The identity-gap guard.** Sequence is an identity column: 100 can commit while 99 is
        // still in flight, so a reader taking MAX(Sequence) as its new watermark skips 99 forever.
        // Waiting out the longest possible ingest transaction is the fix, and ingest is one insert.
        var ceiling = DateTime.UtcNow.AddSeconds(-_options.SafetyLagSeconds);

        var events = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.Sequence > checkpoint.Watermark && e.ReceivedAtUtc <= ceiling)
            .OrderBy(e => e.Sequence)
            .Take(_options.ProjectionBatchSize)
            .ToListAsync(cancellationToken);

        if (events.Count == 0) return 0;

        var userIds = events.Select(e => e.UserId).Distinct().ToList();
        var sessionIds = events.Select(e => e.SessionId).Distinct().ToList();
        var days = events.Select(e => e.DayUtc).Distinct().ToList();

        // Everything the fold will touch, in three queries rather than three per event. At two
        // thousand events a batch the difference is the whole cost of the pass.
        var lifecycles = await _dbContext.TelemetryUserLifecycle
            .Where(l => userIds.Contains(l.UserId))
            .ToDictionaryAsync(l => l.UserId, cancellationToken);

        var sessions = await _dbContext.TelemetrySessions
            .Where(s => sessionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var userDays = await _dbContext.TelemetryUserDays
            .Where(d => userIds.Contains(d.UserId) && days.Contains(d.DayUtc))
            .ToDictionaryAsync(d => (d.UserId, d.DayUtc), cancellationToken);

        var schemas = await _dbContext.TelemetryEventSchemas
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Name, StringComparer.Ordinal, cancellationToken);

        var metrics = new Dictionary<MetricKey, TelemetryDailyMetric>();

        foreach (var e in events)
        {
            var lifecycle = FoldLifecycle(lifecycles, e);
            FoldSession(sessions, e);
            FoldUserDay(userDays, e, lifecycle.CohortDayUtc);
            await FoldMetricsAsync(metrics, schemas, e, cancellationToken);
        }

        checkpoint.Watermark = events[^1].Sequence;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return events.Count;
    }

    /// <summary>
    /// One row per user, ever. **<c>CohortDayUtc</c> only ever moves backwards, and only before a
    /// day has been summarised** — see the note on <see cref="TelemetryUserDay.FirstSeenDayUtc"/>
    /// for why a moving cohort day quietly rewrites history.
    /// </summary>
    private TelemetryUserLifecycle FoldLifecycle(
        Dictionary<Guid, TelemetryUserLifecycle> lifecycles, TelemetryEvent e)
    {
        if (!lifecycles.TryGetValue(e.UserId, out var lifecycle))
        {
            lifecycle = new TelemetryUserLifecycle
            {
                UserId = e.UserId,
                FirstSeenAtUtc = e.ReceivedAtUtc,
                CohortDayUtc = e.DayUtc,
                LastSeenAtUtc = e.ReceivedAtUtc,
                InstallAppVersion = e.AppVersion,
                InstallPlatform = e.Platform,
                LastAppVersion = e.AppVersion,
                LastPlatform = e.Platform
            };

            lifecycles[e.UserId] = lifecycle;
            _dbContext.TelemetryUserLifecycle.Add(lifecycle);
        }

        // The replay guard. Two projectors on two app instances read the same watermark; without
        // this, both add the same batch to these totals.
        if (e.Sequence <= lifecycle.LastSequence) return lifecycle;

        lifecycle.LastSequence = e.Sequence;
        lifecycle.TotalEvents++;

        if (e.ReceivedAtUtc > lifecycle.LastSeenAtUtc)
        {
            lifecycle.LastSeenAtUtc = e.ReceivedAtUtc;

            // Updated freely — this one is about the present, unlike InstallAppVersion, which is
            // stamped once so an upgrade cannot rewrite which build acquired the user.
            lifecycle.LastAppVersion = e.AppVersion;
            lifecycle.LastPlatform = e.Platform;
        }

        if (e.Name == TelemetryNames.SessionStart) lifecycle.TotalSessions++;

        if (e.Name == TelemetryNames.SessionEnd)
            lifecycle.TotalPlaySeconds += ReadInt(e, "duration_s");

        return lifecycle;
    }

    private void FoldSession(Dictionary<Guid, TelemetrySession> sessions, TelemetryEvent e)
    {
        if (!sessions.TryGetValue(e.SessionId, out var session))
        {
            session = new TelemetrySession
            {
                Id = e.SessionId,
                UserId = e.UserId,
                StartedAtUtc = e.OccurredAtUtc,
                LastSeenAtUtc = e.OccurredAtUtc,
                AppVersion = e.AppVersion,
                Platform = e.Platform,
                DayUtc = e.DayUtc
            };

            sessions[e.SessionId] = session;
            _dbContext.TelemetrySessions.Add(session);
        }

        if (e.Sequence <= session.LastSequence) return;

        session.LastSequence = e.Sequence;
        session.EventCount++;

        // Events within a session can arrive out of order — the queue flushes what it has, and a
        // retried batch lands after ones queued behind it. So the bounds widen rather than assign.
        if (e.OccurredAtUtc < session.StartedAtUtc) session.StartedAtUtc = e.OccurredAtUtc;
        if (e.OccurredAtUtc > session.LastSeenAtUtc) session.LastSeenAtUtc = e.OccurredAtUtc;

        if (e.Name != TelemetryNames.SessionEnd) return;

        session.EndedAtUtc = e.OccurredAtUtc;

        // Clamped to the span the events themselves prove. The client's own number is the honest
        // one when it is plausible — it knows about background time the server cannot see — but an
        // unclamped duration is a free multiplier on every play-time figure downstream, which is
        // the same reasoning Run.DurationMs is clamped under.
        var reported = ReadInt(e, "duration_s");
        var observed = (int)Math.Max(0, (session.LastSeenAtUtc - session.StartedAtUtc).TotalSeconds);

        session.DurationSeconds = reported > 0 && reported <= observed + 60 ? reported : observed;
    }

    private void FoldUserDay(
        Dictionary<(Guid, DateTime), TelemetryUserDay> userDays, TelemetryEvent e, DateTime cohortDay)
    {
        var key = (e.UserId, e.DayUtc);

        if (!userDays.TryGetValue(key, out var day))
        {
            day = new TelemetryUserDay
            {
                UserId = e.UserId,
                DayUtc = e.DayUtc,
                FirstSeenDayUtc = cohortDay,

                // Stored, not derived. This one column is why the retention triangle is a group-by
                // over a rollup instead of a self-join over the raw stream. See Rule 4.
                DayIndex = Math.Max(0, (int)(e.DayUtc - cohortDay).TotalDays)
            };

            userDays[key] = day;
            _dbContext.TelemetryUserDays.Add(day);
        }

        if (e.Sequence <= day.LastSequence) return;

        day.LastSequence = e.Sequence;
        day.EventCount++;

        switch (e.Name)
        {
            case TelemetryNames.SessionStart:
                day.SessionCount++;
                break;

            case TelemetryNames.SessionEnd:
                day.PlaySeconds += ReadInt(e, "duration_s");
                break;

            case TelemetryNames.RunStarted:
                day.RunCount++;
                break;

            case TelemetryNames.AttemptSubmitted:
                day.AttemptCount++;
                break;
        }
    }

    /// <summary>
    /// Folds the daily counters — the ungrouped total plus whichever dimensions the schema names.
    /// <para>
    /// **Unregistered events are never folded.** The row is stored so nothing is lost, but a name
    /// nobody has declared does not get to create a metric — otherwise a typo in a shipped build
    /// becomes a permanent series that looks exactly like a real one. See Rule 6.
    /// </para>
    /// </summary>
    private async Task FoldMetricsAsync(
        Dictionary<MetricKey, TelemetryDailyMetric> metrics,
        IReadOnlyDictionary<string, TelemetryEventSchema> schemas,
        TelemetryEvent e,
        CancellationToken cancellationToken)
    {
        if (e.IsUnregistered) return;
        if (!schemas.TryGetValue(e.Name, out var schema) || !schema.RollUpDaily) return;

        await BumpAsync(metrics, e, string.Empty, string.Empty, cancellationToken);

        if (string.IsNullOrWhiteSpace(schema.Dimensions)) return;

        foreach (var dimension in schema.Dimensions.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = dimension.Trim();
            var value = ResolveDimension(e, name);

            // A dimension the event cannot supply is skipped, not stored as empty — an empty
            // DimensionValue is the key of the ungrouped total, and writing one here would add the
            // event to that total twice.
            if (value is null) continue;

            await BumpAsync(metrics, e, name, value, cancellationToken);
        }
    }

    private async Task BumpAsync(
        Dictionary<MetricKey, TelemetryDailyMetric> metrics,
        TelemetryEvent e,
        string dimension,
        string dimensionValue,
        CancellationToken cancellationToken)
    {
        var key = new MetricKey(e.DayUtc, e.Name, dimension, dimensionValue);

        if (!metrics.TryGetValue(key, out var metric))
        {
            metric = await _dbContext.TelemetryDailyMetrics.FirstOrDefaultAsync(
                m => m.DayUtc == e.DayUtc &&
                     m.Name == e.Name &&
                     m.Dimension == dimension &&
                     m.DimensionValue == dimensionValue,
                cancellationToken);

            if (metric is null)
            {
                metric = new TelemetryDailyMetric
                {
                    DayUtc = e.DayUtc,
                    Name = e.Name,
                    Dimension = dimension,
                    DimensionValue = dimensionValue
                };

                _dbContext.TelemetryDailyMetrics.Add(metric);
            }

            metrics[key] = metric;
        }

        if (e.Sequence <= metric.LastSequence) return;

        metric.LastSequence = e.Sequence;

        // Scaled back up by the rate the client actually sampled at, so a series does not collapse
        // the day somebody turns sampling on. The rate is stamped per event for exactly this — the
        // registry's current rate would be the wrong divisor for anything collected before it changed.
        metric.Count += e.SampleRate is > 0 and < 1 ? (long)Math.Round(1 / e.SampleRate) : 1;
    }

    private static string? ResolveDimension(TelemetryEvent e, string dimension) => dimension switch
    {
        TelemetryDimensions.Platform => e.Platform,
        TelemetryDimensions.AppVersion => e.AppVersion,
        TelemetryDimensions.Locale => e.Locale,
        TelemetryDimensions.GameId => e.GameId?.ToString(),
        _ => null
    };

    private readonly record struct MetricKey(DateTime Day, string Name, string Dimension, string Value);

    // ---- the nightly pass -----------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the retention triangle and fills in unique-user counts, for the recent window only.
    /// <para>
    /// **Recent-window rather than everything.** A batch arriving late adds activity to a day that
    /// was already summarised, so the last few weeks are always redone; older cohorts are settled
    /// and rewriting them nightly would be work that changes nothing but the <c>ComputedAtUtc</c>.
    /// </para>
    /// </summary>
    public async Task<int> RunNightlyAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var since = today.AddDays(-_options.NightlyLookbackDays);

        var written = await RebuildCohortsAsync(today, since, cancellationToken);
        written += await ComputeUniqueUsersAsync(today, since, cancellationToken);

        return written;
    }

    private async Task<int> RebuildCohortsAsync(
        DateTime today, DateTime since, CancellationToken cancellationToken)
    {
        // Which cohorts could have moved: every cohort with activity in the lookback window. A user
        // who returned yesterday after two months changes exactly one cell of a two-month-old
        // cohort, and nothing else — so the set of *cohorts* to redo is narrower than the set of days.
        var touchedCohorts = await _dbContext.TelemetryUserDays
            .AsNoTracking()
            .Where(d => d.DayUtc >= since && d.DayUtc <= today)
            .Select(d => d.FirstSeenDayUtc)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (touchedCohorts.Count == 0) return 0;

        var sizes = await _dbContext.TelemetryUserLifecycle
            .AsNoTracking()
            .Where(l => touchedCohorts.Contains(l.CohortDayUtc))
            .GroupBy(l => l.CohortDayUtc)
            .Select(g => new { CohortDay = g.Key, Size = g.Count() })
            .ToDictionaryAsync(x => x.CohortDay, x => x.Size, cancellationToken);

        // The one query the whole feature is designed around. Both grouping columns are stored on
        // the row and indexed together, so this is a seek per cohort rather than a join.
        var cells = await _dbContext.TelemetryUserDays
            .AsNoTracking()
            .Where(d => touchedCohorts.Contains(d.FirstSeenDayUtc) && d.DayIndex <= _options.MaxCohortDayIndex)
            .GroupBy(d => new { d.FirstSeenDayUtc, d.DayIndex })
            .Select(g => new
            {
                g.Key.FirstSeenDayUtc,
                g.Key.DayIndex,
                Retained = g.Count()
            })
            .ToListAsync(cancellationToken);

        var existing = await _dbContext.TelemetryRetentionCohorts
            .Where(c => touchedCohorts.Contains(c.CohortDayUtc))
            .ToDictionaryAsync(c => (c.CohortDayUtc, c.DayIndex), cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var cell in cells)
        {
            var key = (cell.FirstSeenDayUtc, cell.DayIndex);

            if (!existing.TryGetValue(key, out var row))
            {
                row = new TelemetryRetentionCohort
                {
                    CohortDayUtc = cell.FirstSeenDayUtc,
                    DayIndex = cell.DayIndex
                };

                _dbContext.TelemetryRetentionCohorts.Add(row);
                existing[key] = row;
            }

            // Assigned, not incremented. This pass recomputes from the substrate, so it is
            // idempotent by construction — running it twice produces the same triangle, which is
            // what lets it be re-run after a late batch without any guard at all.
            row.CohortSize = sizes.TryGetValue(cell.FirstSeenDayUtc, out var size) ? size : cell.Retained;
            row.RetainedUsers = cell.Retained;
            row.ComputedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return cells.Count;
    }

    /// <summary>
    /// Fills <c>UniqueUsers</c> for the recent window.
    /// <para>
    /// **Not something the streaming projector can do.** A distinct count cannot be folded from a
    /// stream without holding every user seen that day in memory; so the projector fills
    /// <c>Count</c> live and this computes the distinct half over a bounded set of days. Until it
    /// runs, the column is null — which the console renders as "pending", never as zero. A zero
    /// there would be a claim that nobody did the thing.
    /// </para>
    /// </summary>
    private async Task<int> ComputeUniqueUsersAsync(
        DateTime today, DateTime since, CancellationToken cancellationToken)
    {
        var uniques = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.DayUtc >= since && e.DayUtc <= today && !e.IsUnregistered)
            .GroupBy(e => new { e.DayUtc, e.Name })
            .Select(g => new
            {
                g.Key.DayUtc,
                g.Key.Name,
                Users = g.Select(e => e.UserId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        if (uniques.Count == 0) return 0;

        // Only the ungrouped totals get a unique count. A per-dimension distinct count is a
        // multiple of this work for a number nobody has yet asked for, and adding it later is a
        // change to this method rather than to the schema.
        var rows = await _dbContext.TelemetryDailyMetrics
            .Where(m => m.DayUtc >= since && m.DayUtc <= today && m.Dimension == string.Empty)
            .ToListAsync(cancellationToken);

        var byKey = rows.ToDictionary(r => (r.DayUtc, r.Name));
        var now = DateTime.UtcNow;
        var updated = 0;

        foreach (var u in uniques)
        {
            if (!byKey.TryGetValue((u.DayUtc, u.Name), out var row)) continue;

            row.UniqueUsers = u.Users;
            row.UniqueUsersComputedAtUtc = now;
            updated++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Telemetry nightly pass filled {Count} unique-user counts.", updated);

        return updated;
    }

    // ---- payload reading ------------------------------------------------------------------------

    /// <summary>
    /// Reads one integer out of an event's parameter bag.
    /// <para>
    /// Tolerant on purpose — a missing or malformed field returns zero rather than throwing. The
    /// projector runs unattended over events from every build that has ever shipped, and one
    /// malformed payload from a two-year-old client must not stop the pipeline for everybody else.
    /// </para>
    /// </summary>
    private static int ReadInt(TelemetryEvent e, string key)
    {
        if (string.IsNullOrEmpty(e.ParamsJson) || e.ParamsJson == "{}") return 0;

        try
        {
            using var document = JsonDocument.Parse(e.ParamsJson);

            if (!document.RootElement.TryGetProperty(key, out var value)) return 0;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var i) => i,
                JsonValueKind.Number when value.TryGetDouble(out var d) => (int)Math.Round(d),
                JsonValueKind.String when int.TryParse(value.GetString(), out var s) => s,
                _ => 0
            };
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
