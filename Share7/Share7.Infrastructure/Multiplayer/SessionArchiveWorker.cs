using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Share7.Application.Multiplayer.Interfaces;

namespace Share7.Infrastructure.Multiplayer;

public sealed class SessionArchiveWorker(IServiceScopeFactory scopes, ILogger<SessionArchiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISessionArchiveService>().SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Session archival failed; retrying next tick."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
