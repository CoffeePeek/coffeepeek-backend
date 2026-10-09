using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Infrastructure.Consumers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public sealed class ImportCandidatePublishedEventHandlerTests
{
    [Fact]
    public async Task DuplicateCandidateInOneBatch_ReturnsSameShopWithoutCreatingTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var candidateId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var item = new ImportCandidatePublishedItem(
            candidateId, Guid.NewGuid(), "Coffee", "Street 1", 53m, 27m,
            Guid.NewGuid(), null, null, null, CoffeeShopType.Cafe, [], false);
        var service = new Mock<ICreateShopFromImportService>();
        service.Setup(s => s.CreateShopFromImportAsync(item, ct)).ReturnsAsync(shopId);
        var handler = new ImportCandidatePublishedEventHandler(
            service.Object, NullLogger<ImportCandidatePublishedEventHandler>.Instance);

        var result = await handler.Handle(new ImportCandidatePublishedEvent([item, item]), ct);

        Assert.Equal(2, result.Results.Count);
        Assert.All(result.Results, row =>
        {
            Assert.Equal(candidateId, row.CandidateId);
            Assert.Equal(shopId, row.ShopId);
        });
        service.Verify(s => s.CreateShopFromImportAsync(item, ct), Times.Once);
    }
}
