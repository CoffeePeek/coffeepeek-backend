using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Persistence.Data;
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

    private IHost BuildHost() => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context());
                services.AddScoped<IAppDistributionRepository, AppDistributionRepository>();
                services.AddScoped<IUnitOfWork, UnitOfWork<ShopsDbContext>>();
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(AppDownloadRedirectDurabilityLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(RecordAppDownloadRedirectHandler));
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
