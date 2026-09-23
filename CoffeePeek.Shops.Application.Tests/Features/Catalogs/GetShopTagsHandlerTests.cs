using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.Catalogs.GetShopTags;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Catalogs;

public class GetShopTagsHandlerTests
{
    private readonly Mock<IQueryShopTagRepository> _repository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ReturnsActiveTagsFromRepositoryOnCacheMiss()
    {
        var tag = ShopTag.Create("laptop_friendly", "Laptop Friendly", "Good for work", 10);
        _repository.Setup(r => r.GetActiveAsync(_ct)).ReturnsAsync([tag]);

        _cache
            .Setup(c => c.GetAsync(
                It.Is<CacheKey>(k => k.Key == CacheKey.Shop.TagsCatalog().Key),
                It.IsAny<Func<CancellationToken, Task<ShopTagDto[]>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                CacheKey _,
                Func<CancellationToken, Task<ShopTagDto[]>> factory,
                TimeSpan? _,
                CancellationToken ct) => factory(ct));

        var result = await GetShopTagsHandler.Handle(
            new GetShopTagsCommand(),
            _repository.Object,
            _cache.Object,
            _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Tags.Should().ContainSingle();
        result.Data.Tags[0].Slug.Should().Be("laptop_friendly");
        result.Data.Tags[0].Name.Should().Be("Laptop Friendly");
        result.Data.Tags[0].Description.Should().Be("Good for work");
        result.Data.Tags[0].SortOrder.Should().Be(10);
        _repository.Verify(r => r.GetActiveAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheReturnsNull_ReturnsError()
    {
        _cache
            .Setup(c => c.GetAsync(
                It.IsAny<CacheKey>(),
                It.IsAny<Func<CancellationToken, Task<ShopTagDto[]>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShopTagDto[])null);

        var result = await GetShopTagsHandler.Handle(
            new GetShopTagsCommand(),
            _repository.Object,
            _cache.Object,
            _ct);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("shop tags");
        _repository.Verify(r => r.GetActiveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
