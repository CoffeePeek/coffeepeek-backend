using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Shops.ReorderPhotos;
using CoffeePeek.Shops.Application.Features.Owner.ReorderPhotos;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Shops;

public class ReorderPhotosCacheInvalidationTests
{
    private readonly Mock<ICoffeeShopRepository> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly IOptions<MediaPublicUrlOptions> _media = Options.Create(
        new MediaPublicUrlOptions { PublicEndpoint = "https://cdn.example.com" });

    public ReorderPhotosCacheInvalidationTests()
    {
        _cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>())).Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);
    }

    [Fact]
    public async Task AdminReorder_InvalidatesSearchCache()
    {
        var shop = CreateShopWithPhotos();
        var order = shop.ShopPhotos.Reverse().Select(p => p.Id).ToArray();
        _repository.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var result = await ReorderAdminCoffeeShopPhotosHandler.Handle(
            new ReorderAdminCoffeeShopPhotosCommand(shop.Id, order),
            _repository.Object, _unitOfWork.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        VerifySearchInvalidated();
    }

    [Fact]
    public async Task OwnerReorder_InvalidatesSearchCache()
    {
        var ownerId = Guid.NewGuid();
        var shop = CreateShopWithPhotos(ownerId);
        var order = shop.ShopPhotos.Reverse().Select(p => p.Id).ToArray();
        _repository.Setup(r => r.GetByIdForOwnerAsync(shop.Id, ownerId, _ct)).ReturnsAsync(shop);

        var result = await ReorderOwnerCoffeeShopPhotosHandler.Handle(
            new ReorderOwnerCoffeeShopPhotosCommand(shop.Id, ownerId, order),
            _repository.Object, _unitOfWork.Object, _cache.Object, _media, _ct);

        result.IsSuccess.Should().BeTrue();
        VerifySearchInvalidated();
    }

    private static DomainCoffeeShop CreateShopWithPhotos(Guid? ownerId = null)
    {
        var actorId = ownerId ?? Guid.NewGuid();
        var shop = new DomainCoffeeShop(actorId, "Shop", null, PriceRange.Moderate, Guid.NewGuid());
        shop.AddPhotos([
            new ShopPhoto("a.jpg", "image/jpeg", "a", 1, actorId),
            new ShopPhoto("b.jpg", "image/jpeg", "b", 1, actorId)
        ]);
        return shop;
    }

    private void VerifySearchInvalidated() =>
        _cache.Verify(
            c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct),
            Times.Once);
}
