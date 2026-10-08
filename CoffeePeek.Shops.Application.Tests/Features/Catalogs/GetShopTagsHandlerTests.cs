using System.Threading.Tasks;
using CoffeePeek.Shops.Application.Features.Catalogs.GetShopTags;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Catalogs;

public class GetShopTagsHandlerTests
{
    [Fact]
    public async Task Handle_ReadsFreshActiveTagsOnEveryRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IQueryShopTagRepository>();
        repository.SetupSequence(r => r.GetActiveAsync(ct))
            .ReturnsAsync([ShopTag.Create("laptop_friendly", "Laptop Friendly", "Good for work", 10)])
            .ReturnsAsync([ShopTag.Create("outdoor", "Outdoor", "Terrace", 20)]);

        var first = await GetShopTagsHandler.Handle(new GetShopTagsCommand(), repository.Object, ct);
        var second = await GetShopTagsHandler.Handle(new GetShopTagsCommand(), repository.Object, ct);

        first.Data!.Tags.Should().ContainSingle().Which.Slug.Should().Be("laptop_friendly");
        first.Data.Tags[0].Name.Should().Be("Laptop Friendly");
        first.Data.Tags[0].Description.Should().Be("Good for work");
        first.Data.Tags[0].SortOrder.Should().Be(10);
        second.Data!.Tags.Should().ContainSingle().Which.Slug.Should().Be("outdoor");
        repository.Verify(r => r.GetActiveAsync(ct), Times.Exactly(2));
    }
}
