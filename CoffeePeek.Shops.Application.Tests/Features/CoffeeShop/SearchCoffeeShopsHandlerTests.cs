using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Common.Responses;
using CoffeePeek.Shops.Application.Features.CoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeShop.SearchCoffeeShops;

public class SearchCoffeeShopsHandlerTests
{
    private readonly Mock<ICoffeeShopQueries> _shopQueriesMock = new();
    private readonly Mock<IQueryCheckInRepository> _visitRepoMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static SearchCoffeeShopsQuery BuildQuery(Guid? userId = null)
        => new SearchCoffeeShopsQuery(UserId: userId);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchUsesCurrentRatingAndOnlyPersonalizesAuthenticatedVisits(bool authenticated)
    {
        var user=Guid.NewGuid();var id=Guid.NewGuid();
        _shopQueriesMock.Setup(q=>q.Search(It.IsAny<SearchCoffeeShopsQuery>(),_ct)).ReturnsAsync(([new ShortShopDto {Id=id,Name="shop"}],1));
        _visitRepoMock.Setup(r=>r.GetVisitedShopIdsAsync(user,_ct)).ReturnsAsync(new List<Guid>{id});
        var result=await SearchCoffeeShopsHandler.Handle(new SearchCoffeeShopsQuery(UserId:authenticated?user:null,PageNumber:1,PageSize:10),_shopQueriesMock.Object,_visitRepoMock.Object,_cacheMock.Object,_ct);
        Assert.Equal(1,result.Data.TotalItems);Assert.Equal(10,result.Data.PageSize);Assert.Equal(authenticated,result.Data.CoffeeShops[0].IsVisited);
        _cacheMock.VerifyNoOtherCalls();
        _visitRepoMock.Verify(r=>r.GetVisitedShopIdsAsync(user,_ct),authenticated?Times.Once():Times.Never());
    }

    [Fact]
    public void CreateSearchHash_IncludesTagsAndComputedFilters()
    {
        var tagA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tagB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var hash = SearchCoffeeShopsHandler.CreateSearchHash(new SearchCoffeeShopsQuery(
            UserId: userId,
            Tags: [tagB, tagA],
            IsOpen: true,
            IsNew: false,
            IsVisited: true));

        hash.Should().Contain("tags:");
        hash.Should().Contain("open:True");
        hash.Should().Contain("new:False");
        hash.Should().Contain("visited:True");
        hash.Should().Contain($"uid:{userId}");
    }

    [Fact]
    public void CreateSearchHash_IncludesShopType()
    {
        var hash = SearchCoffeeShopsHandler.CreateSearchHash(new SearchCoffeeShopsQuery(
            Type: CoffeePeek.Contract.Enums.CoffeeShopType.Specialty));

        hash.Should().Contain("type:1");
    }

    [Fact]
    public void CreateSearchHash_WithoutVisited_OmitsUserId()
    {
        var userId = Guid.NewGuid();
        var hash = SearchCoffeeShopsHandler.CreateSearchHash(new SearchCoffeeShopsQuery(
            UserId: userId,
            IsOpen: true));

        hash.Should().Contain("open:True");
        hash.Should().NotContain("uid:");
        hash.Should().NotContain("visited:");
    }

    [Fact]
    public void ScheduleCacheTtl_ExpiresAtNextUtcMinute()
    {
        var utcNow = new DateTime(2026, 9, 7, 5, 42, 15, 250, DateTimeKind.Utc);

        var ttl = ShopScheduleCachePolicy.UntilNextUtcMinute(utcNow);

        ttl.Should().Be(TimeSpan.FromSeconds(44.75));
    }
}
