using CoffeePeek.MediaService.Configuration;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantCleanupWorker(IServiceScopeFactory scopes, IOptions<PhotoVariantOptions> options,
    ILogger<PhotoVariantCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.CleanupEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                await scope.ServiceProvider.GetRequiredService<PhotoVariantCleanup>().RunAsync(timeout.Token);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested && error is not OutOfMemoryException)
            {
                logger.LogWarning("Variant cleanup failed; retained intents will be retried.");
            }
        }
    }
}
