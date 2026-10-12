using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopChangeRequestOutboxLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopChangeRequestOutboxLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task ApprovedProfile_RedisFailureAfterCommit_DoesNotRollBackAndRetries()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Approved-" + Guid.NewGuid().ToString("N"));
        var shop = new CoffeeShop(null, "Original", "Old",
            CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Moderate, Guid.NewGuid());
        shop.SetLocation(city.Id, "Street 1", 53m, 27m);
        await using (var seed = fixture.Context())
        {
            seed.Cities.Add(city);
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(ct);
        }

        var requestId = Guid.NewGuid();
        var invalidator = new FailOnceStrictCacheInvalidator(shop.Id, async callbackCt =>
        {
            await using var db = fixture.Context();
            return await db.Shops.AsNoTracking().AnyAsync(
                s => s.Id == shop.Id && s.Description == "Approved" && s.ProfileRevision == 1,
                callbackCt) &&
                await db.AppliedShopChangeRequests.AsNoTracking().AnyAsync(
                    row => row.Id == requestId, callbackCt);
        });
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context());
                services.AddScoped<IApplyApprovedShopMenuChangeService, ApplyApprovedShopMenuChangeService>();
                services.AddSingleton<IUploadedPhotoLookup>(Mock.Of<IUploadedPhotoLookup>());
                services.AddSingleton<IStrictCacheInvalidator>(invalidator);
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(ShopChangeRequestOutboxLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(ShopChangeRequestApprovedHandler));
                options.Discovery.IncludeType(typeof(ShopProfileCacheInvalidationRequestedHandler));
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

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(new ShopChangeRequestApprovedV1(
                requestId, shop.Id, Guid.NewGuid(), ShopChangeSection.Description,
                new ShopChangePayloadDto(Description: "Approved"), Guid.NewGuid(), DateTime.UtcNow), ct);

            Assert.True(await invalidator.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(20), ct),
                "The cache consumer ran before the approved profile commit was visible.");
            await invalidator.Recovered.WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.Equal(2, invalidator.DetailAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
