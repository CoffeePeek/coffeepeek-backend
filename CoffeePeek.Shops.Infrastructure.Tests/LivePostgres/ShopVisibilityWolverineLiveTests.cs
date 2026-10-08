using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
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
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopVisibilityWolverineLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopVisibilityWolverineLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task VisibilityChange_InvalidatesOnlyAfterCommittedRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = new CoffeeShop(null, "Visibility test", null, PriceRange.Cheap, Guid.NewGuid());
        await using (var seed = fixture.Context())
        {
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(ct);
        }

        var invalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var observed = fixture.Context();
                var persisted = await observed.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
                invalidated.TrySetResult(persisted.IsHidden && persisted.ProfileRevision == 1);
            });
        cache.Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context());
                services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
                services.AddSingleton(cache.Object);
                services.AddSingleton<IOptions<MediaPublicUrlOptions>>(Options.Create(
                    new MediaPublicUrlOptions
                    {
                        PublicEndpoint = "https://media.example.test",
                        ShopBucketName = "shops"
                    }));
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(ShopVisibilityWolverineLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(SetAdminCoffeeShopVisibilityHandler));
                options.Discovery.IncludeType(typeof(ShopProfileCacheInvalidationRequestedHandler));
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

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                new SetAdminCoffeeShopVisibilityCommand(shop.Id, Hidden: true), ct);
            Assert.True(response.IsSuccess);
            Assert.Equal(1, response.Data!.ProfileRevision);
            Assert.True(await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        await using var verify = fixture.Context();
        var final = await verify.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
        Assert.True(final.IsHidden);
        Assert.Equal(1, final.ProfileRevision);
    }
}
