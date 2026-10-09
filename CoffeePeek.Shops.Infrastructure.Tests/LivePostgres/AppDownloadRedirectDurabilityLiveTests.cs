using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>Checks the controller fallback's inline Wolverine transaction on real PostgreSQL.</summary>
[Trait("Category", LivePostgresServer.Category)]
public sealed class AppDownloadRedirectDurabilityLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public AppDownloadRedirectDurabilityLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task InlineFallback_CommitsBeforeReturn_AndRepeatEventIdIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = BuildHost();

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var command = new RecordAppDownloadRedirectCommand(
                Guid.NewGuid(), "android", "test-agent", "https://example.test/", "BY");

            await bus.InvokeAsync(command, ct);
            await using (var firstRead = fixture.Context())
            {
                var recorded = await firstRead.AppDownloadRedirectEvents.AsNoTracking()
                    .SingleAsync(e => e.Id == command.EventId, ct);
                Assert.Equal(command.Channel, recorded.Channel);
                Assert.Equal(command.UserAgent, recorded.UserAgent);
            }

            await bus.InvokeAsync(command, ct);
            await using var secondRead = fixture.Context();
            Assert.Equal(1, await secondRead.AppDownloadRedirectEvents.AsNoTracking()
                .CountAsync(e => e.Id == command.EventId, ct));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PublishedAnalytics_IsDeliveredByConfiguredLocalQueue()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = BuildHost();
        await host.StartAsync(ct);
        try
        {
            var command = new RecordAppDownloadRedirectCommand(
                Guid.NewGuid(), "ios", null, null, null);
            await host.Services.GetRequiredService<IMessageBus>().PublishAsync(command);

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                await using var db = fixture.Context();
                if (await db.AppDownloadRedirectEvents.AsNoTracking()
                        .AnyAsync(e => e.Id == command.EventId, ct))
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            }

            throw new TimeoutException("The published redirect event was not recorded by the configured local queue.");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReleaseAndStoreSettings_AreCommittedBeforeWolverineReturns()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = BuildHost();
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var adminId = Guid.NewGuid();
            var versionCode = Random.Shared.Next(1_000_000, int.MaxValue);
            var created = await bus.InvokeAsync<Response<AndroidReleaseDto>>(
                new CreateAndroidReleaseCommand(adminId, "1.0.0", versionCode,
                    "https://example.com/coffeepeek.apk", "coffeepeek.apk", 1000,
                    new string('a', 64), DateTime.UtcNow), ct);
            Assert.True(created.IsSuccess);

            await using (var readRelease = fixture.Context())
            {
                var committed = await readRelease.AndroidReleases.AsNoTracking()
                    .SingleAsync(release => release.Id == created.Data.Id, ct);
                Assert.Equal(versionCode, committed.VersionCode);
                Assert.True(await readRelease.AppDistributionAuditLogs.AsNoTracking()
                    .AnyAsync(log => log.EntityId == created.Data.Id, ct));
            }

            var updated = await bus.InvokeAsync<Response<AdminAppDownloadsDto>>(
                new UpdateAppStoreDownloadCommand(adminId, "https://apps.apple.com/app/coffeepeek", true), ct);
            Assert.True(updated.IsSuccess);

            await using var readSettings = fixture.Context();
            var settings = await readSettings.AppDistributionSettings.AsNoTracking().SingleAsync(ct);
            Assert.True(settings.IosAppStoreEnabled);
            Assert.Equal("https://apps.apple.com/app/coffeepeek", settings.IosAppStoreUrl);
            Assert.True(await readSettings.AppDistributionAuditLogs.AsNoTracking()
                .AnyAsync(log => log.EntityId == AppDistributionSettings.SingletonId, ct));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private IHost BuildHost() => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context());
                services.AddScoped<IAppDistributionRepository, AppDistributionRepository>();
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(AppDownloadRedirectDurabilityLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(RecordAppDownloadRedirectHandler));
                options.Discovery.IncludeType(typeof(CreateAndroidReleaseHandler));
                options.Discovery.IncludeType(typeof(UpdateAppStoreDownloadHandler));
                options.UseRuntimeCompilation();
                options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
                options.PersistMessagesWithPostgresql(fixture.ConnectionString);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options.Policies.UseDurableInboxOnAllListeners();
                options.Policies.UseDurableLocalQueues();
                options.Durability.Mode = DurabilityMode.Solo;
                options.UseEntityFrameworkCoreTransactions();
                options.Policies.AutoApplyTransactions();
            })
            .Build();
}
