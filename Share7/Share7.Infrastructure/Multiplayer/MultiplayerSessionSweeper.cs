using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Runs <see cref="IMultiplayerSweepService"/> on a timer.
/// <para>
/// **A timer and nothing else, deliberately.** Every rule lives in the scoped service so that a test
/// can run one pass and assert on it; putting the logic here would make it reachable only by
/// standing up a host and waiting.
/// </para>
/// <para>
/// A failing pass must never take the worker down with it — the sweep is the last line of defence
/// for every failure mode in this domain, and a background service that has stopped is silent about
/// it. So the loop catches, logs, and waits for the next tick.
/// </para>
/// </summary>
public class MultiplayerSessionSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<MultiplayerSessionSweeper> _logger;

    public MultiplayerSessionSweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<MultiplayerOptions> options,
        ILogger<MultiplayerSessionSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.SweepIntervalSeconds));

        _logger.LogInformation("Multiplayer session sweeper started; interval {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A fresh scope per pass: the DbContext is scoped, and holding one open for the
                // lifetime of the process would accumulate tracked entities forever.
                await using var scope = _scopeFactory.CreateAsyncScope();

                var sweeper = scope.ServiceProvider.GetRequiredService<IMultiplayerSweepService>();
                await sweeper.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Swallowed on purpose. A transient database failure must not end the loop — the
                // next pass picks up everything this one missed, because the sweep is idempotent.
                _logger.LogError(exception, "Multiplayer sweep failed; retrying at the next interval.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Multiplayer session sweeper stopped.");
    }
}
