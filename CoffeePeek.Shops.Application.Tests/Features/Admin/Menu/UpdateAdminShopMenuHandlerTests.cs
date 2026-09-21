using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Menu;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ContractAvailability = CoffeePeek.Contract.Enums.MenuItemAvailability;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Menu;

public class UpdateAdminShopMenuHandlerTests
{
    [Fact]
    public async Task AttachPhotos_InvalidatesShopDetailCache()
    {
        var userId = Guid.NewGuid();
        var shop = new DomainCoffeeShop(userId, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var menu = ShopMenu.Create(shop.Id);
        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(r => r.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var applyMenu = new Mock<IApplyShopMenuService>();
        applyMenu.Setup(s => s.GetOrCreateAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(menu);
        var queryMenu = new Mock<IQueryShopMenuRepository>();
        queryMenu.Setup(r => r.GetByShopIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(menu);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CoffeeDrinkDefinition>());
        var unitOfWork = new Mock<IUnitOfWork>();
        var cache = new Mock<ICacheService>();
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });

        var result = await AttachAdminShopMenuPhotosHandler.Handle(
            new AttachAdminShopMenuPhotosCommand(
                shop.Id,
                [new UploadedPhotoDto("menu.jpg", "image/jpeg", "menu/key", 100)],
                userId),
            shops.Object,
            applyMenu.Object,
            queryMenu.Object,
            drinks.Object,
            unitOfWork.Object,
            cache.Object,
            media,
            CancellationToken.None);

        result.Item1.IsSuccess.Should().BeTrue();
        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.Detail(shop.Id)), Times.Once);
    }

    [Fact]
    public async Task ParseMenu_InvalidatesShopDetailCache()
    {
        var userId = Guid.NewGuid();
        var shop = new DomainCoffeeShop(userId, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var menu = ShopMenu.Create(shop.Id);
        menu.AddPhotos([ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menu/key", 100)]);
        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(r => r.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var menuRepository = new Mock<IShopMenuRepository>();
        menuRepository.Setup(r => r.GetTrackedByShopIdAsync(shop.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(menu);
        var queryMenu = new Mock<IQueryShopMenuRepository>();
        queryMenu.Setup(r => r.GetByShopIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(menu);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CoffeeDrinkDefinition>());
        var cache = new Mock<ICacheService>();
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });

        var result = await ParseAdminShopMenuHandler.Handle(
            new ParseAdminShopMenuCommand(shop.Id, userId),
            shops.Object,
            menuRepository.Object,
            queryMenu.Object,
            drinks.Object,
            new Mock<IUnitOfWork>().Object,
            cache.Object,
            media,
            CancellationToken.None);

        result.Item1.IsSuccess.Should().BeTrue();
        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.Detail(shop.Id)), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplacedItemWithSameNaturalKey_UpdatesCurrentRowWithoutChangingItsId()
    {
        var shopId = Guid.NewGuid();
        var drinkId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var currentMenu = ShopMenu.Create(shopId);
        var staleMenu = ShopMenu.Create(shopId);
        typeof(ShopMenu).GetProperty(nameof(ShopMenu.Id))!.SetValue(staleMenu, currentMenu.Id);
        staleMenu.ApplyParsedItems(
            [ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, 5.5m, 250, MenuItemSource.Parsed)],
            null,
            null,
            null);
        var staleId = staleMenu.Items.Single().Id;

        // Models the row replaced by a parser transaction after a caller loaded staleId.
        currentMenu.ApplyParsedItems(
            [ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, 5.5m, 250, MenuItemSource.Parsed)],
            null,
            null,
            null);
        var replacementId = currentMenu.Items.Single().Id;
        replacementId.Should().NotBe(staleId);

        var shop = new DomainCoffeeShop(userId, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(repository => repository.GetByIdAsync(shopId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shop);

        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(repository => repository.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CoffeeDrinkDefinition.CreateWithId(
                    drinkId,
                    "espresso",
                    "Эспрессо",
                    "Espresso",
                    CoffeeDrinkCategory.Espresso,
                    "эспрессо,espresso",
                    10)
            ]);

        var applyMenu = new Mock<IApplyShopMenuService>();
        applyMenu.Setup(service => service.GetOrCreateAsync(shopId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleMenu);
        applyMenu.Setup(service => service.ApplyManualItemsAsync(
                shopId,
                It.IsAny<IReadOnlyList<ManualShopMenuItemUpdate>>(),
                userId,
                It.IsAny<CancellationToken>()))
            .Returns<Guid, IReadOnlyList<ManualShopMenuItemUpdate>, Guid?, CancellationToken>(
                (_, updates, updatedBy, _) =>
                {
                    foreach (var update in updates)
                    {
                        currentMenu.ApplyManualItem(
                            update.DrinkDefinitionId,
                            update.Availability,
                            update.Price,
                            update.VolumeMl,
                            updatedBy);
                    }

                    return Task.FromResult(currentMenu);
                });

        var queryMenu = new Mock<IQueryShopMenuRepository>();
        queryMenu.Setup(repository => repository.GetByShopIdAsync(shopId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentMenu);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<ICacheService>();
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });
        var command = new UpdateAdminShopMenuCommand(
            shopId,
            [new UpdateShopMenuItemRequest("espresso", ContractAvailability.Present, 7m, 30)],
            false,
            userId);

        var result = await UpdateAdminShopMenuHandler.Handle(
            command,
            shops.Object,
            applyMenu.Object,
            drinks.Object,
            queryMenu.Object,
            unitOfWork.Object,
            cache.Object,
            media,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        currentMenu.Items.Single().Id.Should().Be(replacementId);
        currentMenu.Items.Single().Price.Should().Be(7m);
        applyMenu.Verify(service => service.ApplyManualItemsAsync(
            shopId,
            It.Is<IReadOnlyList<ManualShopMenuItemUpdate>>(updates =>
                updates.Count == 1 && updates[0].DrinkDefinitionId == drinkId),
            userId,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
