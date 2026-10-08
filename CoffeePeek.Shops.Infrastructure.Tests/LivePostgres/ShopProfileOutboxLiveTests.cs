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

    private async Task<CoffeeShop> SeedAsync(CancellationToken ct)
    {
        var shop = new CoffeeShop(null, "Original profile", null, PriceRange.Cheap, Guid.NewGuid());
        await using var db = fixture.Context();
        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct);
        return shop;
    }

    private IHost BuildHost(IStrictCacheInvalidator invalidator) => Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddScoped(_ => fixture.Context());
            services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
            services.AddScoped<IQueryCityRepository, QueryCityRepository>();
            services.AddScoped<IQueryEquipmentRepository, QueryEquipmentRepository>();
            services.AddScoped<IQueryCoffeeBeanRepository, QueryCoffeeBeanRepository>();
            services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
            services.AddScoped<IQueryBrewMethodRepository, QueryBrewMethodRepository>();
            services.AddSingleton(invalidator);
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

    private async Task<long> CountPendingEnvelopesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM wolverine_outgoing_envelopes) + " +
            "(SELECT count(*) FROM wolverine_incoming_envelopes)", connection);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
