using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CoffeePeek.MediaService.Services;

/// <summary>Provision before serving traffic; audit policy outside the photo request path.</summary>
public sealed class CheckInStorageMonitor(IServiceScopeFactory scopes, CheckInStorageState state,
    TimeProvider clock, ILogger<CheckInStorageMonitor> logger) : BackgroundService, IHealthCheck
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IStorageService>().EnsureCheckInBucketPrivateAsync(timeout.Token);
        await AuditAsync(timeout.Token);
        state.RequireReady();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await AuditAsync(timeout.Token);
        }
    }

    internal async Task AuditAsync(CancellationToken ct)
    {
        var observedAt = clock.GetTimestamp();
        try
        {
            using var scope = scopes.CreateScope();
            if (await scope.ServiceProvider.GetRequiredService<IStorageService>().IsCheckInBucketPrivateAsync(ct))
            {
                state.MarkVerified(observedAt);
                return;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // SDK exception messages can contain endpoints/object keys. Log only a bounded failure message.
            logger.LogWarning("Private photo storage policy audit failed.");
        }
        state.Invalidate();
        logger.LogError("Private photo storage readiness revoked. Check provisioning and bucket policy.");
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsReady ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Private photo storage has no recent successful policy audit."));
}
