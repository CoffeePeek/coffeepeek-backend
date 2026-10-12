using System;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Services;

public class ApplyShopMenuServiceTests
{
    [Theory]
    [InlineData(true, 9)]
    [InlineData(false, 5)]
    public async Task Snapshot_PreservesManualEditsOnlyForParserResults(bool preserveManualItems, decimal expectedPrice)
    {
        var ct = TestContext.Current.CancellationToken;
        var shopId = Guid.NewGuid();
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "espresso", "Эспрессо", "Espresso",
            CoffeeDrinkCategory.Espresso, "", 0);
        var menu = ShopMenu.Create(shopId);
        menu.ApplyManualItem(drink.Id, MenuItemAvailability.Present, 9, 30, Guid.NewGuid());
        menu.AddPhotos([ShopMenuPhoto.Create("extra.jpg", "image/jpeg", "menus/extra", 10)]);
        var menus = new Mock<IShopMenuRepository>();
        menus.Setup(x => x.GetTrackedByShopIdAsync(shopId, ct)).ReturnsAsync(menu);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(x => x.GetActiveAsync(ct)).ReturnsAsync([drink]);
        var service = new ApplyShopMenuService(menus.Object, drinks.Object,
            Mock.Of<ICoffeeShopRepository>(), Mock.Of<ICacheService>());
        var snapshot = new ShopMenuSnapshot(DateTime.UtcNow, "BYN", null,
            [new("espresso", Contract.Enums.MenuItemAvailability.Present, 5, 30, Contract.Enums.MenuItemSource.Parsed)],
            [new("menu.jpg", "image/jpeg", "menus/original", 10)]);

        await service.ApplySnapshotAsync(shopId, snapshot, false, null, ct, preserveManualItems);

        menu.Items.Should().ContainSingle().Which.Price.Should().Be(expectedPrice);
        menu.Photos.Should().HaveCount(preserveManualItems ? 2 : 1);
    }
}
