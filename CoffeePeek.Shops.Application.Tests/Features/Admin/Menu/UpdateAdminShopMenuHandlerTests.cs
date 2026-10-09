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
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
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
    public async Task ApplyingSuggestedPriceRange_AdvancesSharedProfileRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = new DomainCoffeeShop(null, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var menu = ShopMenu.Create(shop.Id);
        menu.CompleteParse(null, PriceRange.Expensive, null);
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
            CoffeeDrinkCategory.Espresso, "latte", 1);
        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(x => x.GetByIdAsync(shop.Id, ct)).ReturnsAsync(shop);
        var applyMenu = new Mock<IApplyShopMenuService>();
        applyMenu.Setup(x => x.ApplyManualItemsAsync(shop.Id,
                It.IsAny<IReadOnlyList<ManualShopMenuItemUpdate>>(), It.IsAny<Guid?>(), ct))
            .ReturnsAsync(menu);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(x => x.GetActiveAsync(ct)).ReturnsAsync([drink]);
        var query = new Mock<IQueryShopMenuRepository>();
        query.Setup(x => x.GetByShopIdAsync(shop.Id, ct)).ReturnsAsync(menu);

        var result = await UpdateAdminShopMenuHandler.Handle(
            new UpdateAdminShopMenuCommand(shop.Id,
                [new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 5m, 200)], true, Guid.NewGuid()),
            shops.Object, applyMenu.Object, drinks.Object, query.Object,
            new Mock<IUnitOfWork>().Object,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }), ct);

        result.IsSuccess.Should().BeTrue();
        shop.PriceRange.Should().Be(PriceRange.Expensive);
        shop.ProfileRevision.Should().Be(1);
    }

    [Fact]
    public async Task RepeatedDrink_Returns400ForWholeBatchBeforeApplying()
    {
        var shopId = Guid.NewGuid();
        var shop = new DomainCoffeeShop(null, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(x => x.GetByIdAsync(shopId, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
                CoffeeDrinkCategory.Espresso, "latte", 1)
        ]);
        var applyMenu = new Mock<IApplyShopMenuService>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var result = await UpdateAdminShopMenuHandler.Handle(
            new UpdateAdminShopMenuCommand(shopId,
                [new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 5m, 200),
                 new UpdateShopMenuItemRequest("LATTE", ContractAvailability.Present, 6m, 250)],
                false, Guid.NewGuid()),
            shops.Object, applyMenu.Object, drinks.Object, new Mock<IQueryShopMenuRepository>().Object,
            unitOfWork.Object,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }),
            CancellationToken.None);

        result.StatusCode.Should().Be(400);
        applyMenu.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidLaterRow_Returns400BeforeAnyRepositoryOrWriteCall()
    {
        var invalidRows = new[]
        {
            new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, null, 200),
            new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 5m, null),
            new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 0m, 200),
            new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 5m, -1),
            new UpdateShopMenuItemRequest("latte", ContractAvailability.Absent, 5m, null),
            new UpdateShopMenuItemRequest("latte", ContractAvailability.Unknown, null, 200),
            new UpdateShopMenuItemRequest("latte", (ContractAvailability)99, null, null)
        };
        foreach (var invalid in invalidRows)
        {
            var shops = new Mock<ICoffeeShopRepository>(MockBehavior.Strict);
            var applyMenu = new Mock<IApplyShopMenuService>(MockBehavior.Strict);
            var drinks = new Mock<IQueryCoffeeDrinkRepository>(MockBehavior.Strict);
            var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

            var result = await UpdateAdminShopMenuHandler.Handle(
                new UpdateAdminShopMenuCommand(Guid.NewGuid(),
                    [new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 5m, 200), invalid],
                    false, Guid.NewGuid()),
                shops.Object, applyMenu.Object, drinks.Object, new Mock<IQueryShopMenuRepository>().Object,
                unitOfWork.Object,
                Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }),
                CancellationToken.None);

            result.StatusCode.Should().Be(400);
            shops.VerifyNoOtherCalls();
            drinks.VerifyNoOtherCalls();
            applyMenu.VerifyNoOtherCalls();
            unitOfWork.VerifyNoOtherCalls();
        }
    }

    [Fact]
    public async Task UnknownOrInactiveSlug_Returns400WithoutApplyingValidEarlierRow()
    {
        var shopId = Guid.NewGuid();
        var shop = new DomainCoffeeShop(null, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(x => x.GetByIdAsync(shopId, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(x => x.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
                CoffeeDrinkCategory.Espresso, "латте,latte", 1)
        ]);
        var applyMenu = new Mock<IApplyShopMenuService>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var result = await UpdateAdminShopMenuHandler.Handle(
            new UpdateAdminShopMenuCommand(shopId,
                [new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 5m, 200),
                 new UpdateShopMenuItemRequest("inactive", ContractAvailability.Present, 6m, 250)],
                false, Guid.NewGuid()),
            shops.Object, applyMenu.Object, drinks.Object, new Mock<IQueryShopMenuRepository>().Object,
            unitOfWork.Object,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }),
            CancellationToken.None);

        result.StatusCode.Should().Be(400);
        applyMenu.VerifyNoOtherCalls();
        unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AttachPhotos_QueuesPostCommitCacheInvalidation()
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
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });
        var photoLookup = new Mock<CoffeePeek.Shops.Application.Abstractions.IUploadedPhotoLookup>();
        photoLookup.Setup(p => p.ClaimForShopAsync(userId, shop.Id,
                CoffeePeek.Contract.Dtos.ShopPhotos.ShopPhotoAttachmentKind.Menu,
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoffeePeek.Shops.Application.Abstractions.ShopPhotoClaimResult(true,
                [new CoffeePeek.Contract.Dtos.ShopPhotos.ClaimedShopPhotoDto(Guid.NewGuid(),
                    "menu/key", "verified-menu.jpg", "image/jpeg", 150)]));
        var bus = new Mock<Wolverine.IMessageBus>();

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
            media,
            photoLookup.Object,
            bus.Object,
            CancellationToken.None);

        result.Item1.IsSuccess.Should().BeTrue();
        result.Item2.Should().NotBeNull();
        menu.ActiveOcrJobId.Should().Be(result.Item2!.JobId);
        menu.OcrJobs.Should().ContainSingle();
        menu.Photos.Single().FileName.Should().Be("verified-menu.jpg");
        menu.Photos.Single().SizeBytes.Should().Be(150);
        bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            e => e.ShopId == shop.Id)), Times.Once);
    }

    [Fact]
    public async Task ParseMenu_QueuesPostCommitCacheInvalidation()
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
        var bus = new Mock<Wolverine.IMessageBus>();
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });

        var result = await ParseAdminShopMenuHandler.Handle(
            new ParseAdminShopMenuCommand(shop.Id, userId),
            shops.Object,
            menuRepository.Object,
            queryMenu.Object,
            drinks.Object,
            new Mock<IUnitOfWork>().Object,
            bus.Object,
            media,
            CancellationToken.None);

        result.Item1.IsSuccess.Should().BeTrue();
        result.Item2.Should().NotBeNull();
        menu.ActiveOcrJobId.Should().Be(result.Item2!.JobId);
        menu.OcrJobs.Should().ContainSingle();
        bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            e => e.ShopId == shop.Id)), Times.Once);
    }

    [Fact]
    public async Task RemovePhotos_RemovesOnlyRequestedPhotosAndQueuesInvalidation()
    {
        var userId = Guid.NewGuid();
        var shop = new DomainCoffeeShop(userId, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var menu = ShopMenu.Create(shop.Id);
        var removedPhoto = ShopMenuPhoto.Create("old.jpg", "image/jpeg", "menus/old", 100);
        var remainingPhoto = ShopMenuPhoto.Create("current.jpg", "image/jpeg", "menus/current", 200);
        menu.AddPhotos([removedPhoto, remainingPhoto]);

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
        var unitOfWork = new Mock<IUnitOfWork>();
        var bus = new Mock<Wolverine.IMessageBus>();
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });

        var result = await RemoveAdminShopMenuPhotosHandler.Handle(
            new RemoveAdminShopMenuPhotosCommand(shop.Id, [removedPhoto.Id]),
            shops.Object,
            menuRepository.Object,
            queryMenu.Object,
            drinks.Object,
            unitOfWork.Object,
            bus.Object,
            media,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        menu.Photos.Should().ContainSingle().Which.Should().BeSameAs(remainingPhoto);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            e => e.ShopId == shop.Id)), Times.Once);
    }

    [Fact]
    public async Task RemovePhotos_PhotoFromAnotherMenu_ReturnsBadRequestWithoutSaving()
    {
        var userId = Guid.NewGuid();
        var shop = new DomainCoffeeShop(userId, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var menu = ShopMenu.Create(shop.Id);
        menu.AddPhotos([ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/current", 100)]);

        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(r => r.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var menuRepository = new Mock<IShopMenuRepository>();
        menuRepository.Setup(r => r.GetTrackedByShopIdAsync(shop.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(menu);
        var unitOfWork = new Mock<IUnitOfWork>();
        var bus = new Mock<Wolverine.IMessageBus>();

        var result = await RemoveAdminShopMenuPhotosHandler.Handle(
            new RemoveAdminShopMenuPhotosCommand(shop.Id, [Guid.NewGuid()]),
            shops.Object,
            menuRepository.Object,
            new Mock<IQueryShopMenuRepository>().Object,
            new Mock<IQueryCoffeeDrinkRepository>().Object,
            unitOfWork.Object,
            bus.Object,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        menu.Photos.Should().ContainSingle();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        bus.Verify(b => b.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }

    [Fact]
    public async Task UpdateMenu_CreatesManualMenuWithoutPhotos()
    {
        var shopId = Guid.NewGuid();
        var drinkId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var shop = new DomainCoffeeShop(userId, "Coffee", null, PriceRange.Moderate, Guid.NewGuid());
        var menu = ShopMenu.Create(shopId);
        menu.ApplyManualItem(drinkId, MenuItemAvailability.Present, 4.5m, 250, userId);

        var shops = new Mock<ICoffeeShopRepository>();
        shops.Setup(repository => repository.GetByIdAsync(shopId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shop);
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(repository => repository.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CoffeeDrinkDefinition.CreateWithId(
                    drinkId,
                    "latte",
                    "Латте",
                    "Latte",
                    CoffeeDrinkCategory.Espresso,
                    "латте,latte",
                    10)
            ]);
        var applyMenu = new Mock<IApplyShopMenuService>();
        applyMenu.Setup(service => service.ApplyManualItemsAsync(
                shopId,
                It.IsAny<IReadOnlyList<ManualShopMenuItemUpdate>>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(menu);
        var queryMenu = new Mock<IQueryShopMenuRepository>();
        queryMenu.Setup(repository => repository.GetByShopIdAsync(shopId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(menu);

        var result = await UpdateAdminShopMenuHandler.Handle(
            new UpdateAdminShopMenuCommand(
                shopId,
                [new UpdateShopMenuItemRequest("latte", ContractAvailability.Present, 4.5m, 250)],
                false,
                userId),
            shops.Object,
            applyMenu.Object,
            drinks.Object,
            queryMenu.Object,
            new Mock<IUnitOfWork>().Object,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Menu.Photos.Should().BeEmpty();
        result.Data.Menu.Items.Single(item => item.Slug == "latte").Price.Should().Be(4.5m);
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
