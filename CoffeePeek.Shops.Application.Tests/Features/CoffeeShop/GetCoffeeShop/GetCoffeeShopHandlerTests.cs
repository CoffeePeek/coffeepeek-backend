using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Contract.Dtos.CheckIns;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeShop.GetCoffeeShop;

public class GetCoffeeShopHandlerTests
{
    [Fact]
    public async Task RepeatedRequestsReadCurrentReviewsAndKeepOwnerVisitsPersonal()
    {
        var shop=Guid.NewGuid();var owner=Guid.NewGuid();var queries=new Mock<ICoffeeShopQueries>();
        var visits=new Mock<IQueryCheckInRepository>();var reviews=new Mock<ICheckInQueries>();
        var cache=new Mock<ICacheService>();var mapper=new Mock<IMapper>();
        queries.Setup(q=>q.GetDetailsById(shop,default)).ReturnsAsync(new CoffeeShopDetailsDto {Id=shop,Name="shop"});
        reviews.SetupSequence(r=>r.GetStatsByShopAsync(shop,default)).ReturnsAsync((5m,1)).ReturnsAsync((0m,0));
        reviews.Setup(r=>r.GetPublicAsync(It.IsAny<Guid?>(),null,shop,null,10,null,default)).ReturnsAsync(new PublicCheckInPage([],null));
        visits.Setup(r=>r.Exists(owner,shop,default)).ReturnsAsync(true);
        reviews.Setup(r=>r.GetByUserAndShopAsync(owner,shop,1,10,default)).ReturnsAsync([]);
        var before=await GetCoffeeShopHandler.Handle(new GetCoffeeShopQuery(shop,owner),queries.Object,visits.Object,reviews.Object,default);
        var after=await GetCoffeeShopHandler.Handle(new GetCoffeeShopQuery(shop,null),queries.Object,visits.Object,reviews.Object,default);
        Assert.True(before.Data.ShopDto.IsVisited);Assert.False(after.Data.ShopDto.IsVisited);
        Assert.Equal(5,before.Data.ShopDto.Rating);Assert.Equal(0,after.Data.ShopDto.CheckInCount);
        queries.Verify(q=>q.GetDetailsById(shop,default),Times.Exactly(2));cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingShopFailsWithoutLoadingPrivateVisits()
    {
        var queries=new Mock<ICoffeeShopQueries>();var visits=new Mock<IQueryCheckInRepository>();
        var result=await GetCoffeeShopHandler.Handle(new GetCoffeeShopQuery(Guid.NewGuid(),Guid.NewGuid()),queries.Object,visits.Object,new Mock<ICheckInQueries>().Object,default);
        Assert.False(result.IsSuccess);visits.VerifyNoOtherCalls();
    }
}
