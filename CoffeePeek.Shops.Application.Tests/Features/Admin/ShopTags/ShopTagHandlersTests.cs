using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.ShopTags;
using CoffeePeek.Shops.Application.Features.Admin.Shops.SetShopTags;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using FluentAssertions;
using Moq;
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

public class SetShopTagsHandlerTests
{
    private readonly Mock<ICoffeeShopRepository> _shopRepo = new();
    private readonly Mock<IQueryShopTagRepository> _tagRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_AssignsActiveTags()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        var tagId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        _shopRepo.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);
        _tagRepo.Setup(r => r.AllExistAndActiveAsync(It.IsAny<IReadOnlyCollection<Guid>>(), _ct))
            .ReturnsAsync(true);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>())).Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await SetShopTagsHandler.Handle(
            new SetShopTagsCommand(shop.Id, new[] { tagId }, adminId),
            _shopRepo.Object, _tagRepo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.ShopTags.Should().ContainSingle(t => t.TagId == tagId);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveAsync(CacheKey.Shop.Detail(shop.Id)), Times.Once);
    }

    [Fact]
    public async Task Handle_InactiveOrMissingTags_ReturnsBadRequest()
    {
        var shop = new DomainCoffeeShop(Guid.NewGuid(), "Shop", null, DomainPriceRange.Cheap, Guid.NewGuid());
        _shopRepo.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);
        _tagRepo.Setup(r => r.AllExistAndActiveAsync(It.IsAny<IReadOnlyCollection<Guid>>(), _ct))
            .ReturnsAsync(false);

        var result = await SetShopTagsHandler.Handle(
            new SetShopTagsCommand(shop.Id, new[] { Guid.NewGuid() }, Guid.NewGuid()),
            _shopRepo.Object, _tagRepo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Handle_ShopNotFound_ReturnsNotFound()
    {
        _shopRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainCoffeeShop)null);

        var result = await SetShopTagsHandler.Handle(
            new SetShopTagsCommand(Guid.NewGuid(), Array.Empty<Guid>(), Guid.NewGuid()),
            _shopRepo.Object, _tagRepo.Object, _uow.Object, _cache.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }
}
