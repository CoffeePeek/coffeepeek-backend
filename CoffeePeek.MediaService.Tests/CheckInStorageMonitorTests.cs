using CoffeePeek.MediaService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CheckInStorageMonitorTests
{
    [Fact]
    public async Task PolicyDriftAndAuditFailureRevokeReadinessUntilSuccessfulAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.IsCheckInBucketPrivateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var provider = new ServiceCollection().AddScoped(_ => storage.Object).BuildServiceProvider();
        var state = new CheckInStorageState(TimeProvider.System);
        using var monitor = new CheckInStorageMonitor(provider.GetRequiredService<IServiceScopeFactory>(), state,
            TimeProvider.System, NullLogger<CheckInStorageMonitor>.Instance);
        Assert.False(state.IsReady);
        await monitor.StartAsync(ct);
        Assert.True(state.IsReady);
        await monitor.StopAsync(ct);
        storage.Verify(s => s.EnsureCheckInBucketPrivateAsync(It.IsAny<CancellationToken>()), Times.Once);
        storage.Setup(s => s.IsCheckInBucketPrivateAsync(ct)).ReturnsAsync(false);
        await monitor.AuditAsync(ct);
        Assert.False(state.IsReady);
        Assert.Equal(HealthStatus.Unhealthy, (await monitor.CheckHealthAsync(new(), ct)).Status);
        storage.Setup(s => s.IsCheckInBucketPrivateAsync(ct)).ReturnsAsync(true);
        await monitor.AuditAsync(ct);
        Assert.True(state.IsReady);
        storage.Setup(s => s.IsCheckInBucketPrivateAsync(ct)).ThrowsAsync(new IOException("test outage"));
        await monitor.AuditAsync(ct);
        Assert.False(state.IsReady);
        storage.Verify(s => s.EnsureCheckInBucketPrivateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
