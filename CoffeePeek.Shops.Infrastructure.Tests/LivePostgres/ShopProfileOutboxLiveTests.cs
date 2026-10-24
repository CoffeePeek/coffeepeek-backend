using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using System.Diagnostics.Metrics;
using CoffeePeek.Shops.Infrastructure.Observability;
using CoffeePeek.Shops.Infrastructure.Tests.Faults;
using Moq;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.DeadLetterManagement;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using Wolverine.ErrorHandling;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

/// <summary>
/// Exercises the actual Wolverine EF transaction, PostgreSQL outbox and durable local queue.
/// RabbitMQ routing is a separate integration boundary and is not covered by these tests.
/// </summary>
[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopProfileOutboxLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopProfileOutboxLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task FailureAfterPublish_RollsBackProfileAndOutbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = await SeedAsync(ct);
        var invalidator = new FailOnceStrictCacheInvalidator(shop.Id);
        using var host = BuildHost(invalidator);
        await host.StartAsync(ct);
        try
        {
            var before = await CountPendingEnvelopesAsync(ct);
            var bus = host.Services.GetRequiredService<IMessageBus>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bus.InvokeAsync(new RollbackProfileOutboxProbe(shop.Id), ct));

            await using var db = fixture.Context();
            var persisted = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
            Assert.Equal("Original profile", persisted.Name);
            Assert.Equal(0, persisted.ProfileRevision);
            Assert.Equal(before, await CountPendingEnvelopesAsync(ct));
            Assert.Equal(0, invalidator.DetailAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RedisFailureAfterCommit_DoesNotFailProfileWrite_AndDurableConsumerRetries()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = await SeedAsync(ct);
        var invalidator = new FailOnceStrictCacheInvalidator(shop.Id, async callbackCt =>
        {
            await using var db = fixture.Context();
            return await db.Shops.AsNoTracking().AnyAsync(
                s => s.Id == shop.Id && s.Name == "Committed profile" && s.ProfileRevision == 1,
                callbackCt);
        });
        using var host = BuildHost(invalidator);
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                new UpdateAdminCoffeeShopCommand(shop.Id, "Committed profile", null,
                    CoffeePeek.Contract.Enums.PriceRange.Moderate, null, null, null, null, null), ct);

            Assert.True(response.IsSuccess);
            Assert.True(await invalidator.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(20), ct),
                "The consumer ran before the profile commit was visible from a new DbContext.");
            await using (var db = fixture.Context())
            {
                var persisted = await db.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
                Assert.Equal("Committed profile", persisted.Name);
                Assert.Equal(1, persisted.ProfileRevision);
            }

            // The first consumer attempt throws RedisConnectionException. The handler's
            // production retry policy must redeliver the same durable local message.
            await invalidator.Recovered.WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.Equal(2, invalidator.DetailAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DeadLetterGauge_AndSelectiveReplay_KeepCommittedProfileAndUnselectedFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await SeedAsync(ct);
        var second = await SeedAsync(ct);
        var unavailable = 1;
        var firstRecoveries = 0;
        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>()))
            .Returns(async (CacheKey key, CancellationToken token) =>
            {
                if (Volatile.Read(ref unavailable) == 1) throw new InvalidOperationException("test-cache-unavailable");
                if (key.Key == CacheKey.Shop.Detail(first.Id).Key)
                {
                    await using var read = fixture.Context();
                    Assert.True(await read.Shops.AnyAsync(s => s.Id == first.Id && s.ProfileRevision == 1, token));
                    Interlocked.Increment(ref firstRecoveries);
                }
            });
        cache.Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        double deadLetters = -1;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "CoffeePeek.Shops" && instrument.Name == "shops.durability.dead_letters")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => Volatile.Write(ref deadLetters, value));
        listener.Start();
        using var host = BuildHost(cache.Object, deadLetterGate: true);
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var letters = host.Services.GetRequiredService<IMessageStore>().DeadLetters;
            var query = new DeadLetterEnvelopeQuery { ExceptionMessage = "test-cache-unavailable" };
            await UpdateAsync(first.Id);
            await ShopsFaultControl.UntilAsync(async () => (await letters.QueryAsync(query, ct)).TotalCount == 1, ct);
            var selected = Assert.Single((await letters.QueryAsync(query, ct)).Envelopes).Id;
            await UpdateAsync(second.Id);
            await ShopsFaultControl.UntilAsync(async () => (await letters.QueryAsync(query, ct)).TotalCount == 2, ct);
            var unselected = Assert.Single((await letters.QueryAsync(query, ct)).Envelopes, e => e.Id != selected).Id;
            await ShopsFaultControl.UntilAsync(() =>
            {
                listener.RecordObservableInstruments();
                return Task.FromResult(Volatile.Read(ref deadLetters) == 2);
            }, ct);
            Volatile.Write(ref unavailable, 0);
            await letters.ReplayAsync(new DeadLetterEnvelopeQuery { MessageIds = [selected] }, ct);
            await ShopsFaultControl.UntilAsync(async () => Volatile.Read(ref firstRecoveries) == 1
                && await letters.DeadLetterEnvelopeByIdAsync(selected) is null, ct);
            Assert.NotNull(await letters.DeadLetterEnvelopeByIdAsync(unselected));
            await letters.ReplayAsync(new DeadLetterEnvelopeQuery { MessageIds = [selected] }, ct);
            await using var verify = fixture.Context();
            Assert.Equal(1, (await verify.Shops.SingleAsync(s => s.Id == first.Id, ct)).ProfileRevision);
            Assert.Equal(1, (await verify.Shops.SingleAsync(s => s.Id == second.Id, ct)).ProfileRevision);
            Assert.Equal(1, firstRecoveries);

            async Task UpdateAsync(Guid id) => Assert.True((await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                new UpdateAdminCoffeeShopCommand(id, "DLQ committed", null, Contract.Enums.PriceRange.Moderate,
                    null, null, null, null, null), ct)).IsSuccess);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private async Task<CoffeeShop> SeedAsync(CancellationToken ct)
    {
        var shop = new CoffeeShop(null, "Original profile", null, PriceRange.Cheap, Guid.NewGuid());
        var city = new City("Outbox city " + Guid.NewGuid().ToString("N"));
        shop.SetLocation(city.Id, "Original address", 53m, 27m);
        await using var db = fixture.Context();
        db.Cities.Add(city);
        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct);
        return shop;
    }

    private IHost BuildHost(IStrictCacheInvalidator invalidator, bool deadLetterGate = false) => Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddScoped(_ => fixture.Context());
            services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
                services.AddSingleton(Mock.Of<IAddressGeocoder>());
            services.AddScoped<IQueryCityRepository, QueryCityRepository>();
            services.AddScoped<IQueryEquipmentRepository, QueryEquipmentRepository>();
            services.AddScoped<IQueryCoffeeBeanRepository, QueryCoffeeBeanRepository>();
            services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
            services.AddScoped<IQueryBrewMethodRepository, QueryBrewMethodRepository>();
            services.AddSingleton(invalidator);
            if (deadLetterGate) services.AddHostedService<ShopsDurabilityMetrics>();
            services.AddSingleton<IOptions<MediaPublicUrlOptions>>(Options.Create(
                new MediaPublicUrlOptions
                {
                    PublicEndpoint = "https://media.example.test",
                    ShopBucketName = "shops"
                }));
        })
        .UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ShopProfileOutboxLiveTests).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(UpdateAdminCoffeeShopHandler));
            options.Discovery.IncludeType(typeof(RollbackProfileOutboxProbeHandler));
            options.Discovery.IncludeType(typeof(ShopProfileCacheInvalidationRequestedHandler));
            if (deadLetterGate) options.OnException<InvalidOperationException>().MoveToErrorQueue();
            options.UseRuntimeCompilation();
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
            options.PersistMessagesWithPostgresql(fixture.ConnectionString, "wolverine");
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableLocalQueues();
            options.Durability.Mode = DurabilityMode.Solo;
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
        })
        .Build();

    private async Task<long> CountPendingEnvelopesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM wolverine.wolverine_outgoing_envelopes) + " +
            "(SELECT count(*) FROM wolverine.wolverine_incoming_envelopes)", connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
