using CoffeePeek.Moderation.Application.Features.Import.Menu;
using CoffeePeek.Moderation.Application.Features.Shop.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.Extensions.Options;
using Moq;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public sealed class PublishedMenuRevisionTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(false, -1L)]
    [InlineData(false, 0L)]
    [InlineData(true, null)]
    [InlineData(true, -1L)]
    [InlineData(true, 0L)]
    public async Task PublishedTarget_RequiresObservedRevisionBeforeDraftMutation_AndForwardsValidRevision(bool import, long? expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var unit = new Mock<IUnitOfWork>();
        var valid = expected >= 0;
        if (import)
        {
            var candidate = ShopImportCandidate.FromOsm(new("node/1", "Cafe", "Street", 53, 27,
                null, null, null, null, null, null, null, null, new Dictionary<string, string>()), DateTimeOffset.UtcNow);
            candidate.AttachPublishedShop(Guid.NewGuid());
            var repo = new Mock<IShopImportCandidateRepository>();
            repo.Setup(r => r.GetByIdAsync(candidate.Id, ct)).ReturnsAsync(candidate);
            var (response, outbound) = await UpdateImportCandidateMenuHandler.Handle(
                new(candidate.Id, [], false, Guid.NewGuid(), expected), repo.Object, unit.Object,
                Options.Create(new MediaPublicUrlOptions()), ct);
            Assert.Equal(valid, response.IsSuccess);
            Assert.Equal(valid, candidate.Menu is not null);
            Assert.Equal(valid, outbound is not null);
            if (valid) Assert.Equal(expected, outbound!.ExpectedInputRevision);
            else Assert.Equal(400, response.StatusCode);
        }
        else
        {
            var shop = ModerationShop.Create("Cafe", Guid.NewGuid(), Guid.NewGuid(), null);
            shop.AddShopId(Guid.NewGuid());
            var repo = new Mock<IModerationShopRepository>();
            repo.Setup(r => r.GetByIdAsync(shop.Id, ct)).ReturnsAsync(shop);
            var (response, outbound) = await UpdateModerationShopMenuHandler.Handle(
                new(shop.Id, [], false, Guid.NewGuid(), expected), repo.Object, unit.Object, Mock.Of<IMapper>(), ct);
            Assert.Equal(valid, response.IsSuccess);
            Assert.Equal(valid, shop.Menu is not null);
            Assert.Equal(valid, outbound is not null);
            if (valid) Assert.Equal(expected, outbound!.ExpectedInputRevision);
            else Assert.Equal(400, response.StatusCode);
        }
        unit.Verify(u => u.SaveChangesAsync(ct), valid ? Times.Once : Times.Never);
    }
}
