using System;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Moq;
using Wolverine;
using ContractAvailability = CoffeePeek.Contract.Enums.MenuItemAvailability;
using ContractSource = CoffeePeek.Contract.Enums.MenuItemSource;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;
using ShopPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Tests.Features.Menu;

public sealed class ApplyShopMenuServiceValidationTests
{
    [Fact]
    public async Task SnapshotPriceRangeChange_AdvancesSharedProfileRevisionOnlyWhenValueChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = new DomainCoffeeShop(null, "Coffee", null, ShopPriceRange.Moderate, Guid.NewGuid());
        var menu = ShopMenu.Create(shop.Id);
        var menus = new Mock<IShopMenuRepository>();
        menus.Setup(x => x.GetTrackedByShopIdAsync(shop.Id, ct)).ReturnsAsync(menu);
        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(x => x.GetByIdAsync(shop.Id, ct)).ReturnsAsync(shop);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(x => x.GetActiveAsync(ct)).ReturnsAsync([]);
        var service = new ApplyShopMenuService(menus.Object, drinks.Object, shops.Object, new ShopPriceRangeWriter(),
            new Mock<IMessageBus>().Object);
        var snapshot = new ShopMenuSnapshot(null, "BYN", ContractPriceRange.Expensive, [], []);

        await service.ApplySnapshotAsync(shop.Id, snapshot, true, null, ct);
        Assert.Equal(ShopPriceRange.Expensive, shop.PriceRange);
        Assert.Equal(1, shop.ProfileRevision);

        await service.ApplySnapshotAsync(shop.Id, snapshot, true, null, ct);
        Assert.Equal(1, shop.ProfileRevision);
    }

    [Fact]
    public async Task LegacyParsedSnapshot_SkipsPartialOffer_AndKeepsCompleteOffer()
    {
        var ct = TestContext.Current.CancellationToken;
        var shopId = Guid.NewGuid();
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
            CoffeeDrinkCategory.Espresso, "латте,latte", 1);
        var menu = ShopMenu.Create(shopId);
        var repository = new Mock<IShopMenuRepository>();
        repository.Setup(x => x.GetTrackedByShopIdAsync(shopId, ct)).ReturnsAsync(menu);
        var catalog = new Mock<IQueryCoffeeDrinkRepository>();
        catalog.Setup(x => x.GetActiveAsync(ct)).ReturnsAsync([drink]);
        var bus = new Mock<IMessageBus>();
        var service = new ApplyShopMenuService(repository.Object, catalog.Object,
            Mock.Of<ICoffeeShopRepository>(), new ShopPriceRangeWriter(), bus.Object);

        await service.ApplySnapshotAsync(shopId,
            new ShopMenuSnapshot(DateTime.UtcNow, "BYN", null,
                [new ShopMenuItemSnapshot("latte", ContractAvailability.Present, 1m, null, ContractSource.Parsed),
                 new ShopMenuItemSnapshot("latte", ContractAvailability.Present, 8m, 200, ContractSource.Parsed)],
                []), false, null, ct);

        var item = Assert.Single(menu.Items);
        Assert.Equal(8m, item.Price);
        Assert.Equal(200, item.VolumeMl);
        Assert.Equal(MenuParseStatus.Ready, menu.ParseStatus);
    }

    [Fact]
    public async Task InvalidManualSnapshot_ThrowsBeforeLoadingOrChangingMenu()
    {
        var ct = TestContext.Current.CancellationToken;
        var shopId = Guid.NewGuid();
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
            CoffeeDrinkCategory.Espresso, "латте,latte", 1);
        var repository = new Mock<IShopMenuRepository>(MockBehavior.Strict);
        var catalog = new Mock<IQueryCoffeeDrinkRepository>();
        catalog.Setup(x => x.GetActiveAsync(ct)).ReturnsAsync([drink]);
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var service = new ApplyShopMenuService(repository.Object, catalog.Object,
            Mock.Of<ICoffeeShopRepository>(), new ShopPriceRangeWriter(), bus.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.ApplySnapshotAsync(shopId,
            new ShopMenuSnapshot(DateTime.UtcNow, "BYN", null,
                [new ShopMenuItemSnapshot("latte", ContractAvailability.Present, 8m, 200, ContractSource.Manual),
                 new ShopMenuItemSnapshot("latte", ContractAvailability.Present, -1m, 200, ContractSource.Manual)],
                []), false, null, ct));

        repository.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OlderParseResult_SkipsPartialRowsBeforeDomainMerge()
    {
        var ct = TestContext.Current.CancellationToken;
        var shopId = Guid.NewGuid();
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
            CoffeeDrinkCategory.Espresso, "латте,latte", 1);
        var menu = ShopMenu.Create(shopId);
        var repository = new Mock<IShopMenuRepository>();
        repository.Setup(x => x.GetTrackedByShopIdAsync(shopId, ct)).ReturnsAsync(menu);
        var catalog = new Mock<IQueryCoffeeDrinkRepository>();
        catalog.Setup(x => x.GetActiveAsync(ct)).ReturnsAsync([drink]);
        var service = new ApplyShopMenuService(repository.Object, catalog.Object,
            Mock.Of<ICoffeeShopRepository>(), new ShopPriceRangeWriter(), new Mock<IMessageBus>().Object);

        await service.ApplyParseResultAsync(shopId,
            [new ParsedMenuItemDto("latte", 1m, null, "Latte")], [], null, [],
            DateTime.UtcNow, null, ct);

        Assert.Empty(menu.Items);
        Assert.Equal(MenuParseStatus.Ready, menu.ParseStatus);
    }
}
