using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shops.Application.Features.Admin.Menu;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopMenuManualWriteLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ShopMenuManualWriteLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task WolverineMenuUpdate_CacheFailureAfterRawUpsert_RollsBackSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = ShopsLiveDatabase.NewShop("menu-rollback-" + Guid.NewGuid().ToString("N"));
        string slug;
        Guid drinkId;
        await using (var seed = fixture.Context())
        {
            var drink = await seed.CoffeeDrinkDefinitions.AsNoTracking().Where(d => d.IsActive)
                .OrderBy(d => d.Slug).Select(d => new { d.Id, d.Slug }).FirstAsync(ct);
            slug = drink.Slug;
            drinkId = drink.Id;
            var menu = ShopMenu.Create(shop.Id);
            menu.ApplyManualItems(
                [new ManualShopMenuItemUpdate(drinkId, MenuItemAvailability.Present, 5m, 200)],
                Guid.NewGuid());
            seed.Shops.Add(shop);
            seed.ShopMenus.Add(menu);
            await seed.SaveChangesAsync(ct);
        }

        var probe = new ShopMenuSqlTransactionProbe();
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql =>
                npgsql.ConfigureDataSource(dataSource => dataSource.EnableDynamicJson()))
            .AddInterceptors(new AuditInterceptor(), probe)
            .Options;
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected cache failure after SQL upsert."));
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => new ShopsDbContext(options));
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
                wolverine.ApplicationAssembly = typeof(ShopMenuManualWriteLiveTests).Assembly;
                wolverine.Discovery.DisableConventionalDiscovery();
                wolverine.Discovery.IncludeType(typeof(UpdateAdminShopMenuHandler));
                wolverine.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                wolverine.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
                wolverine.UseEntityFrameworkCoreTransactions();
                wolverine.Policies.AutoApplyTransactions();
            })
            .Build();

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bus.InvokeAsync<Response<AdminShopMenuDto>>(
                    new UpdateAdminShopMenuCommand(shop.Id,
                        [new UpdateShopMenuItemRequest(slug,
                            CoffeePeek.Contract.Enums.MenuItemAvailability.Present, 8m, 300)],
                        ApplySuggestedPriceRange: false, UserId: Guid.NewGuid()), ct));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }

        Assert.True(probe.SawUpsert);
        Assert.True(probe.UpsertHadTransaction);
        cache.Verify(c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), It.IsAny<CancellationToken>()),
            Times.Once);

        await using var verify = fixture.Context();
        var persisted = await verify.ShopMenus.Include(m => m.Items)
            .SingleAsync(m => m.CoffeeShopId == shop.Id, ct);
        Assert.Equal(1, persisted.InputRevision);
        var item = Assert.Single(persisted.Items);
        Assert.Equal(5m, item.Price);
        Assert.Equal(200, item.VolumeMl);
    }

    [Fact]
    public async Task InvalidLaterRow_DoesNotExecuteEarlierSqlUpsert()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = ShopsLiveDatabase.NewShop("menu-validation-" + Guid.NewGuid().ToString("N"));
        var menu = ShopMenu.Create(shop.Id);
        Guid validDrinkId;
        Guid invalidDrinkId;
        await using (var seed = fixture.Context())
        {
            var drinks = await seed.CoffeeDrinkDefinitions.AsNoTracking().OrderBy(d => d.Slug)
                .Take(2).Select(d => d.Id).ToArrayAsync(ct);
            Assert.Equal(2, drinks.Length);
            validDrinkId = drinks[0];
            invalidDrinkId = drinks[1];
            seed.Shops.Add(shop);
            seed.ShopMenus.Add(menu);
            await seed.SaveChangesAsync(ct);
        }

        await using (var writer = fixture.Context())
        {
            var repository = new ShopMenuRepository(writer);
            await Assert.ThrowsAsync<DomainException>(() => repository.ApplyManualItemsAsync(shop.Id,
                [new ManualShopMenuItemUpdate(validDrinkId, MenuItemAvailability.Present, 5m, 200),
                 new ManualShopMenuItemUpdate(invalidDrinkId, MenuItemAvailability.Present, 0m, 250)],
                Guid.NewGuid(), ct));
            // Wolverine's EF middleware saves at the end even for a returned 400.
            Assert.Equal(0, await writer.SaveChangesAsync(ct));
        }

        await using var verify = fixture.Context();
        var persisted = await verify.ShopMenus.Include(m => m.Items)
            .SingleAsync(m => m.CoffeeShopId == shop.Id, ct);
        Assert.Empty(persisted.Items);
        Assert.Null(persisted.UpdatedByUserId);
    }

    [Fact]
    public async Task SqlUpsert_RefreshesPreviouslyTrackedItemsAndAdvancesRevisionOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = ShopsLiveDatabase.NewShop("menu-tracking-" + Guid.NewGuid().ToString("N"));
        Guid drinkId;
        await using (var seed = fixture.Context())
        {
            drinkId = await seed.CoffeeDrinkDefinitions.AsNoTracking()
                .OrderBy(d => d.Slug).Select(d => d.Id).FirstAsync(ct);
            var menu = ShopMenu.Create(shop.Id);
            menu.ApplyManualItems([
                new ManualShopMenuItemUpdate(drinkId, MenuItemAvailability.Present, 5m, 200)
            ], Guid.NewGuid());
            seed.Shops.Add(shop);
            seed.ShopMenus.Add(menu);
            await seed.SaveChangesAsync(ct);
        }

        await using var writer = fixture.Context();
        var tracked = await writer.ShopMenus.Include(m => m.Items)
            .SingleAsync(m => m.CoffeeShopId == shop.Id, ct);
        Assert.Equal(5m, tracked.Items.Single().Price);

        var returned = await new ShopMenuRepository(writer).ApplyManualItemsAsync(shop.Id,
            [new ManualShopMenuItemUpdate(drinkId, MenuItemAvailability.Present, 8m, 300)],
            Guid.NewGuid(), ct);

        Assert.Same(tracked, returned);
        Assert.Equal(2, returned.InputRevision);
        Assert.Equal(8m, returned.Items.Single().Price);
        Assert.Equal(300, returned.Items.Single().VolumeMl);
        Assert.Single(writer.ChangeTracker.Entries<ShopMenuItem>(),
            entry => entry.Entity.ShopMenuId == returned.Id);
    }

    [Fact]
    public async Task DatabaseConstraintRejectsInvalidPresentOfferWhenDomainIsBypassed()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = ShopsLiveDatabase.NewShop("menu-constraint-" + Guid.NewGuid().ToString("N"));
        Guid menuId;
        Guid drinkId;
        await using (var seed = fixture.Context())
        {
            drinkId = await seed.CoffeeDrinkDefinitions.AsNoTracking()
                .OrderBy(d => d.Slug).Select(d => d.Id).FirstAsync(ct);
            var menu = ShopMenu.Create(shop.Id);
            seed.Shops.Add(shop);
            seed.ShopMenus.Add(menu);
            await seed.SaveChangesAsync(ct);
            menuId = menu.Id;
        }

        await using var writer = fixture.Context();
        var now = DateTime.UtcNow;
        var itemId = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<PostgresException>(() => writer.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ShopMenuItems"
                ("Id", "ShopMenuId", "DrinkDefinitionId", "Availability", "Price", "VolumeMl",
                 "Source", "Kind", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({itemId}, {menuId}, {drinkId}, 1, 0, 200, 1, 0, {now}, {now})
            """, ct));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_ShopMenuItems_Values", error.ConstraintName);
    }

    [Fact]
    public async Task ConcurrentMenuCreationReturnsConflictForLosingWriter()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = ShopsLiveDatabase.NewShop("menu-create-race-" + Guid.NewGuid().ToString("N"));
        await using (var seed = fixture.Context())
        {
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(ct);
        }

        await using var first = fixture.Context();
        await using var second = fixture.Context();
        await new ShopMenuRepository(first).ApplyManualItemsAsync(shop.Id, [], Guid.NewGuid(), ct);
        await new ShopMenuRepository(second).ApplyManualItemsAsync(shop.Id, [], Guid.NewGuid(), ct);

        await first.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync(ct));
        await using var verify = fixture.Context();
        Assert.Single(await verify.ShopMenus.Where(menu => menu.CoffeeShopId == shop.Id).ToListAsync(ct));
    }
}
