using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Shared.Kernel.Observability;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantWorker(IServiceScopeFactory scopes, IOptions<PhotoVariantOptions> options,
    ILogger<PhotoVariantWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var jobs = scope.ServiceProvider.GetRequiredService<PhotoVariantJobStore>();
                var job = await jobs.ClaimAsync(stoppingToken);
                if (job != null)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(PhotoVariantOptions.AttemptTimeout);
                    using var measurement = PhotoDeliveryTelemetry.Start("variant_render");
                    try
                    {
                        if (await scope.ServiceProvider.GetRequiredService<PhotoVariantProcessor>().ProcessAsync(job, timeout.Token))
                            measurement.Complete();
                    }
                    catch (Exception error) when (!stoppingToken.IsCancellationRequested && error is not OutOfMemoryException)
                    {
                        var permanent = error is InvalidDataException;
                        await jobs.FailAsync(job, permanent ? "invalid_image" : "render_unavailable", permanent, stoppingToken);
                        logger.LogWarning("Photo variant attempt failed ({FailureCode}); retry state persisted.",
                            permanent ? "invalid_image" : "render_unavailable");
                    }
                    continue;
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested && error is not OutOfMemoryException)
            {
                logger.LogWarning("Photo variant queue unavailable; retrying shortly.");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
