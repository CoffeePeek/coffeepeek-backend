using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Menu;

public class ParseMenuPhotosHandlerTests
{
    private readonly Mock<IMenuVisionParser> _parser = new();
    private readonly Mock<IQueryCoffeeDrinkRepository> _drinks = new();
    private readonly Mock<IMenuPhotoDownloader> _downloader = new();
    private readonly IOptions<MenuPriceRangeOptions> _price = Options.Create(new MenuPriceRangeOptions());
    private readonly CancellationToken _ct = CancellationToken.None;

    public ParseMenuPhotosHandlerTests()
    {
        _drinks.Setup(d => d.GetActiveAsync(_ct)).ReturnsAsync(
        [
            CoffeeDrinkDefinition.CreateWithId(
                CoffeeDrinkIds.Cappuccino, "cappuccino", "Капучино", "Cappuccino",
                CoffeeDrinkCategory.Espresso, "капучино,cappuccino", 40),
            CoffeeDrinkDefinition.CreateWithId(
                CoffeeDrinkIds.V60, "v60", "V60 / воронка", "V60",
                CoffeeDrinkCategory.Filter, "воронка,v60", 110)
        ]);
        _downloader
            .Setup(d => d.DownloadAsync(It.IsAny<IReadOnlyList<MenuPhotoDownloadRequest>>(), _ct))
            .ReturnsAsync([new MenuVisionPhoto([1, 2, 3], "image/jpeg")]);
    }

    [Fact]
    public async Task Handle_KeepsCheapestCompleteOffer_AndSkipsPartialOcrLines()
    {
        _parser.Setup(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), _ct))
            .ReturnsAsync(new MenuVisionParseResult(true, null,
            [
                new VisionDrinkLine("Капучино", 12m, 300, 0.9),
                new VisionDrinkLine("Cappuccino", 8m, 200, 0.8),
                new VisionDrinkLine("Cappuccino", 1m, null, 0.8),
                new VisionDrinkLine("Воронка", 7m, 250, 0.7),
                new VisionDrinkLine("Авторский раф-банан", 12m, 350, 0.4),
                new VisionDrinkLine("Без цены", null, 200, 0.4)
            ]));

        var result = await ParseMenuPhotosHandler.Handle(
            new ParseMenuPhotosCommand([new ParseMenuPhotoInput("menus/a.jpg", "image/jpeg", "http://x/a.jpg")]),
            _parser.Object,
            _drinks.Object,
            _downloader.Object,
            _price,
            NullLogger.Instance,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Success.Should().BeTrue();
        result.Data.Items.Should().ContainSingle(i => i.Slug == "cappuccino" && i.Price == 8m && i.VolumeMl == 200);
        result.Data.Items.Should().Contain(i => i.Slug == "v60" && i.Price == 7m && i.VolumeMl == 250);
        result.Data.Unmatched.Should().Contain(u => u.RawName.Contains("раф-банан"));
        result.Data.Unmatched.Should().NotContain(u => u.RawName == "Без цены");
        result.Data.SuggestedPriceRange.Should().Be(CoffeePeek.Contract.Enums.PriceRange.Moderate);
    }

    [Fact]
    public async Task Handle_ParserFailure_ReturnsFailedPayload()
    {
        _parser.Setup(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), _ct))
            .ReturnsAsync(new MenuVisionParseResult(false, "boom", []));

        var result = await ParseMenuPhotosHandler.Handle(
            new ParseMenuPhotosCommand([new ParseMenuPhotoInput("menus/a.jpg", "image/jpeg", "http://x/a.jpg")]),
            _parser.Object,
            _drinks.Object,
            _downloader.Object,
            _price,
            NullLogger.Instance,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Success.Should().BeFalse();
        result.Data.Error.Should().Be("boom");
        result.Data.Items.Should().BeEmpty();
    }
}
