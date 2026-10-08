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

public class SetShopTagsHandlerTests
{
    private readonly Mock<ICoffeeShopRepository> _shopRepo = new();
    private readonly Mock<IQueryShopTagRepository> _tagRepo = new();
    private readonly Mock<IMessageBus> _bus = new();
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

        var result = await SetShopTagsHandler.Handle(
            new SetShopTagsCommand(shop.Id, new[] { tagId }, adminId),
            _shopRepo.Object, _tagRepo.Object, _bus.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        shop.ShopTags.Should().ContainSingle(t => t.TagId == tagId);
        shop.ProfileRevision.Should().Be(1);
        _bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            message => message.ShopId == shop.Id)), Times.Once);
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
            _shopRepo.Object, _tagRepo.Object, _bus.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShopNotFound_ReturnsNotFound()
    {
        _shopRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainCoffeeShop)null);

        var result = await SetShopTagsHandler.Handle(
            new SetShopTagsCommand(Guid.NewGuid(), Array.Empty<Guid>(), Guid.NewGuid()),
            _shopRepo.Object, _tagRepo.Object, _bus.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }
}
