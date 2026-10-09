using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShopMenuManualWriteValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedDrink_IsRejectedBeforeSqlOrTrackedMutation(bool menuExists)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var shopId = Guid.NewGuid();
        await using var db = new ShopsDbContext(options);
        if (menuExists)
        {
            db.ShopMenus.Add(ShopMenu.Create(shopId));
            await db.SaveChangesAsync(ct);
        }

        var drinkId = Guid.NewGuid();
        var updates = new[]
        {
            new ManualShopMenuItemUpdate(drinkId, MenuItemAvailability.Present, 5m, 200),
            new ManualShopMenuItemUpdate(drinkId, MenuItemAvailability.Present, 6m, 250)
        };

        await Assert.ThrowsAsync<DomainException>(() =>
            new ShopMenuRepository(db).ApplyManualItemsAsync(shopId, updates, Guid.NewGuid(), ct));
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(menuExists ? 1 : 0, await db.ShopMenus.CountAsync(ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidLaterRow_IsRejectedBeforeSqlOrTrackedMutation(bool menuExists)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var shopId = Guid.NewGuid();
        await using var db = new ShopsDbContext(options);
        if (menuExists)
        {
            db.ShopMenus.Add(ShopMenu.Create(shopId));
            await db.SaveChangesAsync(ct);
        }

        var repository = new ShopMenuRepository(db);
        var updates = new[]
        {
            new ManualShopMenuItemUpdate(Guid.NewGuid(), MenuItemAvailability.Present, 5m, 200),
            new ManualShopMenuItemUpdate(Guid.NewGuid(), MenuItemAvailability.Present, -1m, 200)
        };

        await Assert.ThrowsAsync<DomainException>(() =>
            repository.ApplyManualItemsAsync(shopId, updates, Guid.NewGuid(), ct));
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(menuExists ? 1 : 0, await db.ShopMenus.CountAsync(ct));
    }

    [Fact]
    public async Task NewMenuBatchAdvancesRevisionOnceForTwoDrinks()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ShopsDbContext(options);
        var menu = await new ShopMenuRepository(db).ApplyManualItemsAsync(Guid.NewGuid(),
            [new ManualShopMenuItemUpdate(Guid.NewGuid(), MenuItemAvailability.Present, 5m, 200),
             new ManualShopMenuItemUpdate(Guid.NewGuid(), MenuItemAvailability.Present, 8m, 300)],
            Guid.NewGuid(), ct);

        Assert.Equal(1, menu.InputRevision);
        Assert.Equal(2, menu.Items.Count);
        Assert.All(menu.Items, item => Assert.Equal(MenuItemSource.Manual, item.Source));
    }

    [Fact]
    public async Task PhotoWithMediaIdentity_IsTrackedAsNewOnPersistedMenu()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ShopsDbContext(options);
        var menu = ShopMenu.Create(Guid.NewGuid());
        db.ShopMenus.Add(menu);
        await db.SaveChangesAsync(ct);

        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "uploads/menu.jpg", 123,
            Guid.NewGuid());
        menu.AddPhotos([photo]);
        db.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Added, db.Entry(photo).State);
        await db.SaveChangesAsync(ct);
        Assert.Single(await db.Set<ShopMenuPhoto>().ToListAsync(ct));
    }
}
