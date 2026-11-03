namespace CoffeePeek.MediaService.Services;

public sealed class CollectionCoverCleanupWorker(IServiceScopeFactory scopes, ILogger<CollectionCoverCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CollectionCoverCleanup>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Expired collection cover cleanup will retry on the next interval."); }
        }
    }
}
