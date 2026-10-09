using System;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeShop.SearchCoffeeShops;

public sealed class SearchCoffeeShopsHandlerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchUsesCurrentRatingAndOnlyPersonalizesAuthenticatedVisits(bool authenticated)
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var shops = new Mock<ICoffeeShopQueries>();
        shops.Setup(q => q.Search(It.IsAny<SearchCoffeeShopsQuery>(), ct))
            .ReturnsAsync(([new ShortShopDto { Id = shopId, Name = "shop" }], 1));
        var visits = new Mock<IQueryCheckInRepository>();
        visits.Setup(r => r.GetVisitedShopIdsAsync(userId, ct)).ReturnsAsync([shopId]);

        var result = await SearchCoffeeShopsHandler.Handle(
            new SearchCoffeeShopsQuery(UserId: authenticated ? userId : null, PageNumber: 1, PageSize: 10),
            shops.Object, visits.Object, ct);

        Assert.Equal(1, result.Data.TotalItems);
        Assert.Equal(10, result.Data.PageSize);
        Assert.Equal(authenticated, result.Data.CoffeeShops[0].IsVisited);
        visits.Verify(r => r.GetVisitedShopIdsAsync(userId, ct), authenticated ? Times.Once() : Times.Never());
    }
}
