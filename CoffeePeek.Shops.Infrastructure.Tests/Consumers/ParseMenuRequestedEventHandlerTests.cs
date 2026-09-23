using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class ParseMenuRequestedEventHandlerTests
{
    private readonly Mock<IApplyShopMenuService> _applyMenu = new();
    private readonly Mock<IQueryCoffeeShopRepository> _shopRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly IOptions<MediaPublicUrlOptions> _mediaOptions =
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });

    private readonly Mock<IMenuVisionParser> _parser = new();
    private readonly Mock<IQueryCoffeeDrinkRepository> _drinks = new();
    private readonly Mock<IMenuPhotoDownloader> _downloader = new();
    private readonly IOptions<MenuPriceRangeOptions> _priceOptions = Options.Create(new MenuPriceRangeOptions());
    private readonly CancellationToken _ct = CancellationToken.None;

    public ParseMenuRequestedEventHandlerTests()
    {
        _drinks.Setup(d => d.GetActiveAsync(_ct)).ReturnsAsync(
        [
            CoffeeDrinkDefinition.CreateWithId(
                CoffeeDrinkIds.Cappuccino, "cappuccino", "Капучино", "Cappuccino",
                CoffeeDrinkCategory.Espresso, "капучино,cappuccino", 40)
        ]);

        _downloader
            .Setup(d => d.DownloadAsync(It.IsAny<IReadOnlyList<MenuPhotoDownloadRequest>>(), _ct))
            .ReturnsAsync([new MenuVisionPhoto([1, 2, 3], "image/jpeg")]);

        _parser
            .Setup(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), _ct))
            .ReturnsAsync(new MenuVisionParseResult(true, null,
            [
                new VisionDrinkLine("Cappuccino", 8m, null, 0.9)
            ]));

        _applyMenu
            .Setup(a => a.ApplyParseResultAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<ParsedMenuItemDto>>(),
                It.IsAny<IReadOnlyList<UnmatchedMenuItemDto>>(),
                It.IsAny<ContractPriceRange?>(),
                It.IsAny<IReadOnlyList<ShopMenuPhotoSnapshot>>(),
                It.IsAny<DateTime>(),
                It.IsAny<Guid?>(),
                _ct))
            .Returns(Task.CompletedTask);
    }

    private ParseMenuRequestedEventHandler CreateHandler() =>
        new(
            _applyMenu.Object,
            _shopRepository.Object,
            _unitOfWork.Object,
            _mediaOptions,
            _parser.Object,
            _drinks.Object,
            _downloader.Object,
            _priceOptions,
            NullLogger<ParseMenuRequestedEventHandler>.Instance);

    private static ParseMenuRequestedEvent CreateMessage(Guid shopId) =>
        new(
            MenuParseSourceKind.PublishedShop,
            Guid.NewGuid(),
            shopId,
            [new MenuPhotoRef("a.jpg", "image/jpeg", "menus/a.jpg", 123)],
            null);

    [Fact]
    public async Task Handle_ConcurrencyConflictOnSave_ClearsTrackingAndRetriesWithoutReparsing()
    {
        var shopId = Guid.NewGuid();
        var message = CreateMessage(shopId);
        var saveAttempts = 0;

        _unitOfWork
            .Setup(u => u.SaveChangesAsync(_ct))
            .Returns(() =>
            {
                saveAttempts++;
                if (saveAttempts == 1)
                {
                    throw new ConflictException(
                        "A database conflict occurred. The record may already exist.",
                        new DbUpdateConcurrencyException());
                }

                return Task.FromResult(1);
            });

        var handler = CreateHandler();

        var result = await handler.Handle(message, _ct);

        result.Success.Should().BeTrue();
        result.Items.Should().Contain(i => i.Slug == "cappuccino");

        _parser.Verify(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), _ct), Times.Once);
        _applyMenu.Verify(a => a.ApplyParseResultAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<ParsedMenuItemDto>>(),
            It.IsAny<IReadOnlyList<UnmatchedMenuItemDto>>(),
            It.IsAny<ContractPriceRange?>(),
            It.IsAny<IReadOnlyList<ShopMenuPhotoSnapshot>>(),
            It.IsAny<DateTime>(),
            It.IsAny<Guid?>(),
            _ct), Times.Exactly(2));
        _unitOfWork.Verify(u => u.SaveChangesAsync(_ct), Times.Exactly(2));
        _unitOfWork.Verify(u => u.ClearTracking(), Times.Once);
    }

    [Fact]
    public async Task Handle_ConcurrencyConflictExceedsMaxAttempts_FailsWithoutRetriableConflict()
    {
        var shopId = Guid.NewGuid();
        var message = CreateMessage(shopId);

        _unitOfWork
            .Setup(u => u.SaveChangesAsync(_ct))
            .Returns(() => throw new ConflictException(
                "A database conflict occurred. The record may already exist.",
                new DbUpdateConcurrencyException()));

        var handler = CreateHandler();

        var act = async () => await handler.Handle(message, _ct);

        // Must not surface as a transient DB conflict, or Wolverine re-runs the handler and bills another parse.
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        CoffeePeek.Shared.Persistence.Extensions.TransientDbConflict.Matches(thrown).Should().BeFalse();

        _parser.Verify(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), _ct), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(_ct), Times.Exactly(3));
        _unitOfWork.Verify(u => u.ClearTracking(), Times.Exactly(2));
    }
}
