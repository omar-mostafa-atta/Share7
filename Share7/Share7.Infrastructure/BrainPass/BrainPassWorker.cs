using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Share7.Application.BrainPass;

namespace Share7.Infrastructure.BrainPass;

public sealed class BrainPassWorker(IServiceScopeFactory scopes, ILogger<BrainPassWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IBrainPassService>().ProjectPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Brain Pass projection failed; retrying next tick."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
