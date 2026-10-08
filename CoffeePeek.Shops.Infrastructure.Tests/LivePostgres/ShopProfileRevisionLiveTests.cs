using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Menu;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using JasperFx;
using JasperFx.CodeGeneration;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopProfileRevisionLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopProfileRevisionLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentLegacyProfileCommands_OneReturnsConflictWithoutWolverineRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Original", null, PriceRange.Cheap, Guid.NewGuid());
        shop.AssignOwner(ownerId);
        await SeedShopAsync(shop, ct);

        var barrier = new ShopProfileReadBarrierInterceptor();
        var options = new DbContextOptionsBuilder<CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql =>
                npgsql.ConfigureDataSource(dataSource => dataSource.EnableDynamicJson()))
            .AddInterceptors(new AuditInterceptor(), barrier)
            .Options;
        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<CoffeePeek.Shared.Domain.Interfaces.Infrastructure.CacheKey>(),
                It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        cache.Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => new CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext(options));
                services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
                services.AddScoped<IQueryCityRepository, QueryCityRepository>();
                services.AddScoped<IQueryEquipmentRepository, QueryEquipmentRepository>();
                services.AddScoped<IQueryCoffeeBeanRepository, QueryCoffeeBeanRepository>();
                services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
                services.AddScoped<IQueryBrewMethodRepository, QueryBrewMethodRepository>();
                services.AddSingleton(cache.Object);
                services.AddSingleton<IOptions<MediaPublicUrlOptions>>(Options.Create(
                    new MediaPublicUrlOptions
                    {
                        PublicEndpoint = "https://media.example.test",
                        ShopBucketName = "shops"
                    }));
            })
            .UseWolverine(wolverine =>
            {
                wolverine.ApplicationAssembly = typeof(ShopProfileRevisionLiveTests).Assembly;
                wolverine.Discovery.DisableConventionalDiscovery();
                wolverine.Discovery.IncludeType(typeof(UpdateAdminCoffeeShopHandler));
                wolverine.Discovery.IncludeType(typeof(UpdateOwnerCoffeeShopHandler));
                wolverine.Discovery.IncludeType(typeof(ShopProfileCacheInvalidationRequestedHandler));
                wolverine.UseRuntimeCompilation();
                wolverine.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                wolverine.CodeGeneration.AlwaysUseServiceLocationFor(
                    typeof(CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext));
                wolverine.PersistMessagesWithPostgresql(fixture.ConnectionString);
                wolverine.Policies.UseDurableOutboxOnAllSendingEndpoints();
                wolverine.Policies.UseDurableInboxOnAllListeners();
                wolverine.Policies.UseDurableLocalQueues();
                wolverine.Durability.Mode = DurabilityMode.Solo;
                wolverine.UseEntityFrameworkCoreTransactions();
                wolverine.Policies.AutoApplyTransactions();
                // Match the production policy: an unconverted EF concurrency error retries.
                wolverine.Policies.OnException(TransientDbConflict.Matches)
                    .RetryWithCooldown(TimeSpan.FromMilliseconds(10));
            })
            .Build();

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            async Task<Exception?> InvokeAdminAsync()
            {
                try
                {
                    await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                        new UpdateAdminCoffeeShopCommand(shop.Id, "Admin edit", null,
                            CoffeePeek.Contract.Enums.PriceRange.Cheap, null, null, null, null, null), ct);
                    return null;
                }
                catch (Exception error)
                {
                    return error;
                }
            }

            async Task<Exception?> InvokeOwnerAsync()
            {
                try
                {
                    await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
                        new UpdateOwnerCoffeeShopCommand(shop.Id, ownerId, "Owner edit", null,
                            null, null, null, null, null, null, null), ct);
                    return null;
                }
                catch (Exception error)
                {
                    return error;
                }
            }

            var outcomes = await Task.WhenAll(InvokeAdminAsync(), InvokeOwnerAsync());
            // The barrier synchronizes the first two reads; profile includes and catalog loading
            // may issue further Shops queries, so their total is not a retry counter.
            Assert.Single(outcomes, error => error is null);
            Assert.Single(outcomes, error => error is ConflictException);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        await using var verify = fixture.Context();
        var persisted = await verify.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
        Assert.Contains(persisted.Name, new[] { "Admin edit", "Owner edit" });
        Assert.Equal(1, persisted.ProfileRevision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminAndOwnerLoadedSameRevision_SecondCommitConflicts(bool adminFirst)
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Original", null, PriceRange.Cheap, Guid.NewGuid());
        shop.AssignOwner(ownerId);
        await SeedShopAsync(shop, ct);

        await using var adminDb = fixture.Context();
        await using var ownerDb = fixture.Context();
        var media = Options.Create(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.test", ShopBucketName = "shops"
        });
        var bus = Mock.Of<IMessageBus>();
        var admin = await UpdateAdminCoffeeShopHandler.Handle(
            new UpdateAdminCoffeeShopCommand(shop.Id, "Admin edit", null,
                CoffeePeek.Contract.Enums.PriceRange.Moderate, null, null, null, null, null)
            { ExpectedRevision = 0 },
            new CoffeeShopRepository(adminDb), Mock.Of<IQueryCityRepository>(),
            Mock.Of<IQueryEquipmentRepository>(), Mock.Of<IQueryCoffeeBeanRepository>(),
            Mock.Of<IQueryRoasterRepository>(), Mock.Of<IQueryBrewMethodRepository>(), bus, media, ct);
        var owner = await UpdateOwnerCoffeeShopHandler.Handle(
            new UpdateOwnerCoffeeShopCommand(shop.Id, ownerId, "Owner edit", null,
                null, null, null, null, null, null, null)
            { ExpectedRevision = 0 },
            new CoffeeShopRepository(ownerDb), Mock.Of<IQueryCityRepository>(),
            Mock.Of<IQueryEquipmentRepository>(), Mock.Of<IQueryCoffeeBeanRepository>(),
            Mock.Of<IQueryRoasterRepository>(), Mock.Of<IQueryBrewMethodRepository>(), bus, media, ct);
        Assert.Equal(1, admin.Data.ProfileRevision);
        Assert.Equal(1, owner.Data.ProfileRevision);

        if (adminFirst)
        {
            await adminDb.SaveChangesAsync(ct);
            await Assert.ThrowsAsync<ConflictException>(() => ownerDb.SaveChangesAsync(ct));
        }
        else
        {
            await ownerDb.SaveChangesAsync(ct);
            await Assert.ThrowsAsync<ConflictException>(() => adminDb.SaveChangesAsync(ct));
        }

        await using var verify = fixture.Context();
        var persisted = await verify.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
        Assert.Equal(adminFirst ? "Admin edit" : "Owner edit", persisted.Name);
        Assert.Equal(1, persisted.ProfileRevision);
    }

    [Fact]
    public async Task StaleConditionalOwnerWrite_DoesNotChangeCommittedAdminProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Original", null, PriceRange.Cheap, Guid.NewGuid());
        shop.AssignOwner(ownerId);
        await SeedShopAsync(shop, ct);

        var media = Options.Create(new MediaPublicUrlOptions
        {
            PublicEndpoint = "https://media.example.test", ShopBucketName = "shops"
        });
        await using (var adminDb = fixture.Context())
        {
            await UpdateAdminCoffeeShopHandler.Handle(
                new UpdateAdminCoffeeShopCommand(shop.Id, "Admin edit", null,
                    CoffeePeek.Contract.Enums.PriceRange.Moderate, null, null, null, null, null)
                { ExpectedRevision = 0 },
                new CoffeeShopRepository(adminDb), Mock.Of<IQueryCityRepository>(),
                Mock.Of<IQueryEquipmentRepository>(), Mock.Of<IQueryCoffeeBeanRepository>(),
                Mock.Of<IQueryRoasterRepository>(), Mock.Of<IQueryBrewMethodRepository>(),
                Mock.Of<IMessageBus>(), media, ct);
            await adminDb.SaveChangesAsync(ct);
        }

        await using var staleDb = fixture.Context();
        await Assert.ThrowsAsync<ConflictException>(() => UpdateOwnerCoffeeShopHandler.Handle(
            new UpdateOwnerCoffeeShopCommand(shop.Id, ownerId, "Stale owner edit", null,
                null, null, null, null, null, null, null)
            { ExpectedRevision = 0 },
            new CoffeeShopRepository(staleDb), Mock.Of<IQueryCityRepository>(),
            Mock.Of<IQueryEquipmentRepository>(), Mock.Of<IQueryCoffeeBeanRepository>(),
            Mock.Of<IQueryRoasterRepository>(), Mock.Of<IQueryBrewMethodRepository>(),
            Mock.Of<IMessageBus>(), media, ct));
        Assert.False(staleDb.ChangeTracker.HasChanges());

        await using var verify = fixture.Context();
        var persisted = await verify.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
        Assert.Equal("Admin edit", persisted.Name);
        Assert.Equal(1, persisted.ProfileRevision);
    }

    [Fact]
    public async Task ApprovedModerationChange_RejectsStaleOwnerRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Original", "Old", PriceRange.Cheap, Guid.NewGuid());
        shop.AssignOwner(ownerId);
        await SeedShopAsync(shop, ct);

        await using (var moderationDb = fixture.Context())
        {
            var cache = new Mock<ICacheService>();
            cache.Setup(x => x.RemoveAsync(It.IsAny<CacheKey>())).Returns(Task.CompletedTask);
            cache.Setup(x => x.RemoveByPattern(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0);
            var bus = Mock.Of<IMessageBus>();
            var lookup = Mock.Of<IUploadedPhotoLookup>();
            var handler = new ShopChangeRequestApprovedHandler(moderationDb, cache.Object,
                NullLogger<ShopChangeRequestApprovedHandler>.Instance,
                new ApplyApprovedShopMenuChangeService(moderationDb, cache.Object, lookup, bus),
                lookup, bus);
            var failure = await handler.Handle(new ShopChangeRequestApprovedV1(
                Guid.NewGuid(), shop.Id, Guid.NewGuid(), CoffeePeek.Contract.Enums.ShopChangeSection.Description,
                new ShopChangePayloadDto(Description: "Approved"), Guid.NewGuid(), DateTime.UtcNow), ct);
            Assert.Null(failure);
        }

        await using var staleDb = fixture.Context();
        await Assert.ThrowsAsync<ConflictException>(() => UpdateOwnerCoffeeShopHandler.Handle(
            new UpdateOwnerCoffeeShopCommand(shop.Id, ownerId, "Stale owner edit", null,
                null, null, null, null, null, null, null) { ExpectedRevision = 0 },
            new CoffeeShopRepository(staleDb), Mock.Of<IQueryCityRepository>(),
            Mock.Of<IQueryEquipmentRepository>(), Mock.Of<IQueryCoffeeBeanRepository>(),
            Mock.Of<IQueryRoasterRepository>(), Mock.Of<IQueryBrewMethodRepository>(),
            Mock.Of<IMessageBus>(), Options.Create(new MediaPublicUrlOptions
            {
                PublicEndpoint = "https://media.example.test", ShopBucketName = "shops"
            }), ct));

        await using var verify = fixture.Context();
        var persisted = await verify.Shops.AsNoTracking().SingleAsync(s => s.Id == shop.Id, ct);
        Assert.Equal("Approved", persisted.Description);
        Assert.Equal(1, persisted.ProfileRevision);
    }

    private async Task SeedShopAsync(CoffeeShop shop, CancellationToken ct)
    {
        var city = new City("Revision city " + Guid.NewGuid().ToString("N"));
        shop.SetLocation(city.Id, "Original address", 53m, 27m);
        await using var seed = fixture.Context();
        seed.Cities.Add(city);
        seed.Shops.Add(shop);
        await seed.SaveChangesAsync(ct);
    }
}
