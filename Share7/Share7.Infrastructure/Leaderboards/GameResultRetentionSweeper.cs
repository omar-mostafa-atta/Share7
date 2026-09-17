using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Runs <see cref="IGameResultRetentionService"/> on a timer.
/// <para>
/// **A timer and nothing else**, exactly like <c>MultiplayerSessionSweeper</c>: every rule lives in
/// the scoped service so a test can run one pass and assert on it, rather than standing up a host
/// and waiting.
/// </para>
/// </summary>
public class GameResultRetentionSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LeaderboardOptions _options;
    private readonly ILogger<GameResultRetentionSweeper> _logger;

    public GameResultRetentionSweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<LeaderboardOptions> options,
        ILogger<GameResultRetentionSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ResultRetentionDays <= 0)
        {
            _logger.LogInformation("Game result retention is off; nothing will be deleted.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(5, _options.RetentionIntervalMinutes));

        _logger.LogInformation(
            "Game result retention started; keeping {Days} days, sweeping every {Interval}.",
            _options.ResultRetentionDays, interval);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var retention = scope.ServiceProvider.GetRequiredService<IGameResultRetentionService>();

                // Batches until a pass comes back short, so a backlog drains over one tick instead of
                // one batch per interval — but bounded, so a first run against years of history
                // cannot hold the table for the whole night.
                for (var pass = 0; pass < _options.MaxRetentionPassesPerTick; pass++)
                {
                    var deleted = await retention.SweepAsync(stoppingToken);

                    if (deleted < _options.RetentionBatchSize)
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Never take the worker down. A retention pass that failed once is a table that grows
                // for another hour; a worker that stopped is a table that grows forever, silently.
                _logger.LogError(exception, "Game result retention pass failed; retrying next tick.");
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
