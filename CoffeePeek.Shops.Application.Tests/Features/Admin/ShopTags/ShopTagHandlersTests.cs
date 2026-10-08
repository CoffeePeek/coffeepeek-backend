using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.ShopTags;
using CoffeePeek.Shops.Application.Features.Admin.Shops.SetShopTags;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using FluentAssertions;
using Moq;
using Wolverine;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.ShopTags;

public class CreateShopTagHandlerTests
{
    private readonly Mock<IShopTagRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_CreatesTagAndInvalidatesCache()
    {
        _repo.Setup(r => r.GetBySlugAsync("laptop_friendly", _ct)).ReturnsAsync((ShopTag)null);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await CreateShopTagHandler.Handle(
            new CreateShopTagCommand("Laptop Friendly", "Laptop Friendly", null, 1),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Slug.Should().Be("laptop_friendly");
        _repo.Verify(r => r.Add(It.IsAny<ShopTag>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Shop.TagsCatalogPattern(), _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateSlug_ReturnsConflict()
    {
        var existing = ShopTag.Create("specialty", "Specialty");
        _repo.Setup(r => r.GetBySlugAsync("specialty", _ct)).ReturnsAsync(existing);

        var result = await CreateShopTagHandler.Handle(
            new CreateShopTagCommand("specialty", "Specialty", null),
            _repo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _repo.Verify(r => r.Add(It.IsAny<ShopTag>()), Times.Never);
    }
}
