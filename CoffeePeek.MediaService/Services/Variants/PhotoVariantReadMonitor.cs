using CoffeePeek.MediaService.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Services.Variants;

public sealed class PhotoVariantReadMonitor(IServiceScopeFactory scopes, PhotoVariantReadState state,
    IOptions<PhotoVariantOptions> options, TimeProvider clock, ILogger<PhotoVariantReadMonitor> logger)
    : BackgroundService, IHealthCheck
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.ReadEnabled) return;
        await AuditAsync(cancellationToken);
        state.Lease.RequireReady();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await AuditAsync(stoppingToken);
    }

    internal async Task AuditAsync(CancellationToken ct)
    {
        var observedAt = clock.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPhotoVariantReader>().AuditPrivacyAsync(timeout.Token);
            state.Lease.MarkVerified(observedAt);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            state.Lease.Invalidate();
            logger.LogWarning("Private variant storage policy audit failed; downloads disabled.");
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(!options.Value.ReadEnabled || state.Lease.IsReady ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Private variant storage has no recent successful policy audit."));
}
