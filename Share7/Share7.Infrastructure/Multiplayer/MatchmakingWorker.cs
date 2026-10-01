using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Runs <see cref="IMatchmakingTicketService.FormMatchesAsync"/> every couple of seconds. A timer and
/// nothing else, like the session sweeper: every rule is in the scoped service, which a test can run
/// one pass of. Every instance runs one; the pass's own lock lets exactly one of them form at a time.
/// </summary>
public class MatchmakingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<MatchmakingWorker> _logger;

    public MatchmakingWorker(IServiceScopeFactory scopeFactory, IOptions<MultiplayerOptions> options, ILogger<MatchmakingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(0.5, _options.TicketIntervalSeconds)));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMatchmakingTicketService>().FormMatchesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed pass must not end matchmaking: the next tick picks up every ticket this one missed.
                _logger.LogError(exception, "Matchmaking pass failed; retrying at the next tick.");
            }
        }
    }
}
