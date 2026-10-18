using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services.Variants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoVariantReadMonitorTests
{
    [Fact]
    public async Task PolicyFailureRevokesReadinessAndDisabledRolloutNeedsNoStorageCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = new Mock<IPhotoVariantReader>();
        reader.Setup(r => r.AuditPrivacyAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var provider = new ServiceCollection().AddSingleton(reader.Object).BuildServiceProvider();
        var state = new PhotoVariantReadState(TimeProvider.System);
        var options = new PhotoVariantOptions();
        using var monitor = new PhotoVariantReadMonitor(provider.GetRequiredService<IServiceScopeFactory>(), state,
            Options.Create(options), TimeProvider.System, NullLogger<PhotoVariantReadMonitor>.Instance);
        await monitor.StartAsync(ct);
        reader.VerifyNoOtherCalls();
        Assert.Equal(HealthStatus.Healthy, (await monitor.CheckHealthAsync(new(), ct)).Status);
        options.ReadEnabled = true;
        await monitor.AuditAsync(ct); Assert.True(state.Lease.IsReady);
        reader.Setup(r => r.AuditPrivacyAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("unavailable"));
        await monitor.AuditAsync(ct); Assert.False(state.Lease.IsReady);
        Assert.Equal(HealthStatus.Unhealthy, (await monitor.CheckHealthAsync(new(), ct)).Status);
        reader.Setup(r => r.AuditPrivacyAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await monitor.AuditAsync(ct); Assert.True(state.Lease.IsReady);
    }
}
