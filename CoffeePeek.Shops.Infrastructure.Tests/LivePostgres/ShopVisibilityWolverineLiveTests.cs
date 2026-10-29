using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Persistance.Ownership;
using CoffeePeek.Shops.Application.Features.Admin.Shops.SetShopTags;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
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
    public async Task ProfileWriteSlices_InvalidateOnlyAfterCommittedRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = new CoffeeShop(null, "Visibility test", null, PriceRange.Cheap, Guid.NewGuid());
        var city = new City("Visibility city " + Guid.NewGuid().ToString("N"));
        shop.SetLocation(city.Id, "Visibility address", 53m, 27m);
        var tag = ShopTag.Create("live-tag-" + Guid.NewGuid().ToString("N"), "Live tag");
        await using (var seed = fixture.Context())
        {
            seed.Cities.Add(city);
            seed.Shops.Add(shop);
            seed.ShopTags.Add(tag);
            await seed.SaveChangesAsync(ct);
        }

        var invalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var focusInvalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tagsInvalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerInvalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerId = Guid.NewGuid();
        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var observed = fixture.Context();
                var persisted = await observed.Shops.Include(s => s.ShopTags).AsNoTracking()
                    .SingleAsync(s => s.Id == shop.Id, ct);
                invalidated.TrySetResult(persisted.IsHidden && persisted.ProfileRevision == 1);
                if (persisted.ProfileRevision == 2)
                    focusInvalidated.TrySetResult(persisted.Type == CoffeeFocusType.Specialty);
                if (persisted.ProfileRevision == 3)
                    tagsInvalidated.TrySetResult(persisted.ShopTags.Single().TagId == tag.Id);
                if (persisted.ProfileRevision == 4)
                    ownerInvalidated.TrySetResult(persisted.OwnerUserId == ownerId);
            });
        cache.Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context());
                services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
                services.AddScoped<IBusinessOwnershipStore, BusinessOwnershipStore>();
                services.AddScoped<BusinessOwnershipService>();
                services.AddScoped<IRoasterRepository, RoasterRepository>();
                services.AddScoped<IRoasterOwnershipWriter, RoasterOwnershipWriter>();
                services.AddSingleton(Mock.Of<IUserExistenceLookup>(x =>
                    x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)));
                services.AddScoped<IQueryShopTagRepository, QueryShopTagRepository>();
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
                options.Discovery.IncludeType(typeof(SetAdminCoffeeShopFocusHandler));
                options.Discovery.IncludeType(typeof(SetShopTagsHandler));
                options.Discovery.IncludeType(typeof(AssignCoffeeShopOwnerHandler));
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

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                new SetAdminCoffeeShopVisibilityCommand(shop.Id, Hidden: true), ct);
            Assert.True(response.IsSuccess);
            Assert.Equal(1, response.Data!.ProfileRevision);
            Assert.True(await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));

            var focus = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                new SetAdminCoffeeShopFocusCommand(shop.Id, CoffeePeek.Contract.Enums.CoffeeShopType.Specialty, Guid.NewGuid()), ct);
            Assert.True(focus.IsSuccess);
            Assert.Equal(2, focus.Data!.ProfileRevision);
            Assert.True(await focusInvalidated.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));

            var repeated = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                new SetAdminCoffeeShopFocusCommand(shop.Id, CoffeePeek.Contract.Enums.CoffeeShopType.Specialty, Guid.NewGuid()), ct);
            Assert.True(repeated.IsSuccess);
            Assert.Equal(2, repeated.Data!.ProfileRevision);

            var assigned = await bus.InvokeAsync<Response>(
                new SetShopTagsCommand(shop.Id, [tag.Id], Guid.NewGuid()), ct);
            Assert.True(assigned.IsSuccess);
            Assert.True(await tagsInvalidated.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));

            var ownership = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                new AssignCoffeeShopOwnerCommand(shop.Id, ownerId, Guid.NewGuid()), ct);
            Assert.True(ownership.IsSuccess);
            Assert.Equal(4, ownership.Data!.ProfileRevision);
            Assert.True(await ownerInvalidated.Task.WaitAsync(TimeSpan.FromSeconds(10), ct));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        await using var verify = fixture.Context();
        var final = await verify.Shops.Include(s => s.ShopTags).AsNoTracking()
            .SingleAsync(s => s.Id == shop.Id, ct);
        Assert.True(final.IsHidden);
        Assert.Equal(4, final.ProfileRevision);
        Assert.Equal(CoffeeFocusType.Specialty, final.Type);
        Assert.Equal(tag.Id, Assert.Single(final.ShopTags).TagId);
        Assert.Equal(ownerId, final.OwnerUserId);
    }
}
