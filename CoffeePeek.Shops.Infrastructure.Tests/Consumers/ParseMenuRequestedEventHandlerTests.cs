using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public class ParseMenuRequestedEventHandlerTests
{
    private readonly Mock<IMenuVisionParser> parser = new();
    private readonly Mock<IQueryCoffeeDrinkRepository> drinks = new();
    private readonly Mock<IMenuPhotoDownloader> downloader = new();

    private ParseMenuRequestedEventHandler Handler() => new(
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }),
        parser.Object, drinks.Object, downloader.Object, Options.Create(new MenuPriceRangeOptions()),
        NullLogger<ParseMenuRequestedEventHandler>.Instance);

    private static ParseMenuRequestedEvent Message(MenuParseSourceKind kind) => new(
        kind, Guid.NewGuid(), Guid.NewGuid(), [new MenuPhotoRef("a.jpg", "image/jpeg", "menus/a.jpg", 123)], null);

    [Theory]
    [InlineData(MenuParseSourceKind.ModerationShop)]
    [InlineData(MenuParseSourceKind.ImportCandidate)]
    public async Task LegacyDraftRequest_ReturnsResultToDraftWithoutAPublishedMenuWriteDependency(MenuParseSourceKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        drinks.Setup(d => d.GetActiveAsync(ct)).ReturnsAsync([
            CoffeeDrinkDefinition.CreateWithId(CoffeeDrinkIds.Cappuccino, "cappuccino", "Капучино", "Cappuccino",
                CoffeeDrinkCategory.Espresso, "капучино,cappuccino", 40)]);
        downloader.Setup(d => d.DownloadAsync(It.IsAny<IReadOnlyList<MenuPhotoDownloadRequest>>(), ct))
            .ReturnsAsync([new MenuVisionPhoto([1, 2, 3], "image/jpeg")]);
        parser.Setup(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), ct))
            .ReturnsAsync(new MenuVisionParseResult(true, null, [new("Cappuccino", 8m, 250, .9)]));
        var message = Message(kind) with { ParseRequestId = Guid.NewGuid() };
        var result = Assert.IsType<MenuParsedEvent>(await Handler().Handle(message, ct));
        Assert.True(result.Success);
        Assert.Equal(message.SourceId, result.SourceId);
        Assert.Equal(message.ParseRequestId, result.ParseRequestId);
        Assert.Equal(message.PublishedShopId, result.PublishedShopId);
        Assert.Equal("cappuccino", Assert.Single(result.Items).Slug);
        parser.Verify(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), ct), Times.Once);
        Assert.DoesNotContain(typeof(ParseMenuRequestedEventHandler).GetConstructors().Single().GetParameters(),
            p => p.ParameterType == typeof(CoffeePeek.Shops.Application.Services.IApplyShopMenuService));
    }

    [Fact]
    public async Task LegacyPublishedRequest_IsQuarantinedBeforePaidParse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = Assert.IsType<MenuParsedEvent>(await Handler().Handle(Message(MenuParseSourceKind.PublishedShop), ct));
        Assert.False(result.Success);
        parser.VerifyNoOtherCalls(); downloader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LegacyEnvelopeWithJobId_ForwardsWithoutPaidParse()
    {
        var ct = TestContext.Current.CancellationToken;
        var jobId = Guid.NewGuid();
        var message = Message(MenuParseSourceKind.PublishedShop) with { JobId = jobId };
        Assert.Equal(jobId, Assert.IsType<ParseMenuOcrJobRequestedEvent>(await Handler().Handle(message, ct)).JobId);
        parser.VerifyNoOtherCalls(); downloader.VerifyNoOtherCalls();
    }
}
