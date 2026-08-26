using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Telemetry;

/// <summary>
/// Ticks <see cref="ITelemetryRollupService"/>.
/// <para>
/// **A timer and nothing else**, exactly like <c>GameResultRetentionSweeper</c> and
/// <c>MultiplayerSessionSweeper</c>: every rule lives in the scoped service so a test can run one
/// pass and assert on it, rather than standing up a host and waiting.
/// </para>
/// </summary>
public class TelemetryProjectorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelemetryOptions _options;
    private readonly ILogger<TelemetryProjectorWorker> _logger;

    /// <summary>
    /// When the nightly pass last ran. Held in memory rather than persisted: missing one after a
    /// restart costs a single extra pass over a bounded window, and a table to remember it would be
    /// more machinery than the problem has.
    /// </summary>
    private DateTime _lastNightlyUtc = DateTime.MinValue;

    public TelemetryProjectorWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<TelemetryOptions> options,
        ILogger<TelemetryProjectorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Telemetry is disabled; the projector will not run.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.ProjectionIntervalSeconds));
        var nightly = TimeSpan.FromHours(Math.Max(1, _options.NightlyIntervalHours));

        _logger.LogInformation(
            "Telemetry projector started; folding every {Interval}, nightly pass every {Nightly}.",
            interval, nightly);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var rollups = scope.ServiceProvider.GetRequiredService<ITelemetryRollupService>();

                // Batches until a pass comes back short, so a backlog drains over one tick rather
                // than one batch per tick — but bounded, so a first run against a weekend of
                // accumulated events cannot hold the tables for the whole night.
                for (var pass = 0; pass < _options.MaxProjectionPassesPerTick; pass++)
                {
                    var folded = await rollups.ProjectAsync(stoppingToken);
                    if (folded < _options.ProjectionBatchSize) break;
                }

                if (DateTime.UtcNow - _lastNightlyUtc >= nightly)
                {
                    await rollups.RunNightlyAsync(stoppingToken);
                    _lastNightlyUtc = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Swallowed and retried on the next tick. An unhandled exception out of ExecuteAsync
                // kills the BackgroundService for the life of the process — the projector would stop
                // silently and the console would show a lag that only ever grows.
                _logger.LogError(ex, "Telemetry projection pass failed; retrying next tick.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>
/// Deletes raw events past their retention. **Rollups are never swept** — they are what keeps a
/// ten-year-old cohort answerable after its events are gone.
/// </summary>
public class TelemetryRetentionSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelemetryOptions _options;
    private readonly ILogger<TelemetryRetentionSweeper> _logger;

    public TelemetryRetentionSweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<TelemetryOptions> options,
        ILogger<TelemetryRetentionSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _options.BehaviouralRetentionDays <= 0)
        {
            _logger.LogInformation("Telemetry retention is off; no raw events will be deleted.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(5, _options.RetentionIntervalMinutes));

        _logger.LogInformation(
            "Telemetry retention started; keeping {Behavioural}d behavioural and {Operational}d operational.",
            _options.BehaviouralRetentionDays, _options.OperationalRetentionDays);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var retention = scope.ServiceProvider.GetRequiredService<ITelemetryRetentionService>();

                for (var pass = 0; pass < _options.MaxRetentionPassesPerTick; pass++)
                {
                    var deleted = await retention.SweepAsync(stoppingToken);
                    if (deleted < _options.RetentionBatchSize) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telemetry retention pass failed; retrying next tick.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>
/// One bounded delete pass over the raw table.
/// <para>
/// **The sweep must never outrun the projector.** Deleting an event the projector has not folded
/// yet loses it from every rollup permanently — no amount of re-running fixes it, because the
/// source row is gone. So the floor is the lower of the retention cutoff and the watermark, and
/// the sweep simply does less work while the projector is behind.
/// </para>
/// </summary>
public class TelemetryRetentionService : ITelemetryRetentionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TelemetryOptions _options;
    private readonly ILogger<TelemetryRetentionService> _logger;

    public TelemetryRetentionService(
        ApplicationDbContext dbContext,
        IOptions<TelemetryOptions> options,
        ILogger<TelemetryRetentionService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;

        var behaviouralCutoff = today.AddDays(-_options.BehaviouralRetentionDays);
        var operationalCutoff = today.AddDays(-Math.Max(1, _options.OperationalRetentionDays));

        var watermark = await _dbContext.ProjectionCheckpoints
            .AsNoTracking()
            .Where(c => c.Consumer == Domain.Leaderboards.ProjectionConsumers.Telemetry)
            .Select(c => (long?)c.Watermark)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        // Per-schema overrides, so an operator can keep a launch funnel's events for four hundred
        // days and an fps sample for a fortnight without either decision touching the other.
        var overrides = await _dbContext.TelemetryEventSchemas
            .AsNoTracking()
            .Where(s => s.RetentionDays != null)
            .ToDictionaryAsync(s => s.Name, s => s.RetentionDays!.Value, StringComparer.Ordinal, cancellationToken);

        var deleted = 0;

        // The default sweep: everything past its category's cutoff, that the projector has already
        // folded, and that no schema override protects.
        var overriddenNames = overrides.Keys.ToList();

        var expired = await _dbContext.TelemetryEvents
            .Where(e => e.Sequence <= watermark)
            .Where(e => !overriddenNames.Contains(e.Name))
            .Where(e => e.Category == TelemetryCategory.Operational
                ? e.DayUtc < operationalCutoff
                : e.DayUtc < behaviouralCutoff)
            .OrderBy(e => e.Sequence)
            .Take(_options.RetentionBatchSize)
            .ToListAsync(cancellationToken);

        if (expired.Count > 0)
        {
            _dbContext.TelemetryEvents.RemoveRange(expired);
            await _dbContext.SaveChangesAsync(cancellationToken);
            deleted += expired.Count;
        }

        // Then each override, one name at a time. A name at a time rather than one clever predicate
        // because the alternative is a CASE over a dictionary that EF cannot translate, and doing it
        // in memory would mean loading the table.
        foreach (var (name, days) in overrides)
        {
            if (deleted >= _options.RetentionBatchSize) break;

            var cutoff = today.AddDays(-Math.Max(1, days));

            var rows = await _dbContext.TelemetryEvents
                .Where(e => e.Name == name && e.DayUtc < cutoff && e.Sequence <= watermark)
                .OrderBy(e => e.Sequence)
                .Take(_options.RetentionBatchSize - deleted)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0) continue;

            _dbContext.TelemetryEvents.RemoveRange(rows);
            await _dbContext.SaveChangesAsync(cancellationToken);
            deleted += rows.Count;
        }

        if (deleted > 0)
            _logger.LogInformation("Telemetry retention removed {Count} raw events.", deleted);

        return deleted;
    }
}
