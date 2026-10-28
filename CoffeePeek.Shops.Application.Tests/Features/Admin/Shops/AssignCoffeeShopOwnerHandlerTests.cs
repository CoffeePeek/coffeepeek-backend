using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Shops;

public sealed class AssignCoffeeShopOwnerHandlerTests
{
    private static readonly IOptions<MediaPublicUrlOptions> MediaOptions = Options.Create(
        new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test", ShopBucketName = "shops" });

    [Fact]
    public async Task ChangedOwner_AdvancesRevisionAndQueuesInvalidation()
    {
        var shop = NewShop();
        var ownerId = Guid.NewGuid();
        var repository = new Mock<ICoffeeShopRepository>();
        repository.Setup(x => x.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var bus = new Mock<IMessageBus>();
        var ownership = CreateOwnership(shop, repository.Object, bus.Object);

        var result = await AssignCoffeeShopOwnerHandler.Handle(
            new AssignCoffeeShopOwnerCommand(shop.Id, ownerId, Guid.NewGuid()), repository.Object, ownership,
            MediaOptions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ownerId, shop.OwnerUserId);
        Assert.Equal(1, shop.ProfileRevision);
        bus.Verify(x => x.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            message => message.ShopId == shop.Id)), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedOwner_DoesNotAdvanceRevisionOrQueueInvalidation(bool emptyGuid)
    {
        var shop = NewShop();
        var repository = new Mock<ICoffeeShopRepository>();
        repository.Setup(x => x.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        var bus = new Mock<IMessageBus>();
        var ownership = CreateOwnership(shop, repository.Object, bus.Object);

        var result = await AssignCoffeeShopOwnerHandler.Handle(
            new AssignCoffeeShopOwnerCommand(shop.Id, emptyGuid ? Guid.Empty : null, Guid.NewGuid()),
            repository.Object, ownership, MediaOptions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(shop.OwnerUserId);
        Assert.Equal(0, shop.ProfileRevision);
        bus.Verify(x => x.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }

    private static DomainCoffeeShop NewShop() =>
        new(Guid.NewGuid(), "Owner test", null, PriceRange.Cheap, Guid.NewGuid());

    private static BusinessOwnershipService CreateOwnership(DomainCoffeeShop shop, ICoffeeShopRepository repository, IMessageBus bus)
    {
        var store = new Mock<IBusinessOwnershipStore>();
        store.Setup(x => x.GetTargetAsync(BusinessType.CoffeeShop, shop.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BusinessOwnershipTargetDto(BusinessType.CoffeeShop, shop.Id, shop.Name, "owner-test", shop.OwnerUserId));
        var users = new Mock<IUserExistenceLookup>();
        users.Setup(x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new(store.Object, repository, Mock.Of<IRoasterOwnershipWriter>(), users.Object, bus);
    }
}
