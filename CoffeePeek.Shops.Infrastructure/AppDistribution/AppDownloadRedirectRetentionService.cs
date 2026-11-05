using CoffeePeek.Shops.Application.Features.AppDownloads;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.AppDistribution;

public sealed class AppDownloadRedirectRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AppDownloadAnalyticsRetentionOptions> options,
    TimeProvider clock,
    ILogger<AppDownloadRedirectRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not delete expired app download redirect analytics");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeleteExpiredAsync(CancellationToken ct)
    {
        var cutoffUtc = clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAppDistributionRepository>();
        var deleted = await repository.DeleteRedirectEventsBeforeAsync(cutoffUtc, ct);
        if (deleted > 0)
            logger.LogInformation("Deleted {DeletedCount} expired app download redirect analytics events", deleted);
    }
}
