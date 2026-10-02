using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeZones;

public sealed class PublicZoneColorTests
{
    [Fact]
    public async Task Map_PreservesZoneColor()
    {
        var id = Guid.NewGuid();
        var address = new PublicAddressMetadata(id, "center", "/coffee-zones/center", 1);
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(a => a.GetForReadAsync(PublicAddressKind.CoffeeZone,
                It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { id })), It.IsAny<CancellationToken>()))
            .ReturnsAsync([address]);
        addresses.Setup(a => a.GetForReadAsync(PublicAddressKind.CoffeeShop,
                It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new PublicDiscoveryService(addresses.Object,
            Mock.Of<IProfileAddressLookup>(), Mock.Of<ICoffeeShopQueries>(),
            Mock.Of<IQueryCheckInRepository>(), Mock.Of<IMessageBus>());
        var zone = new MapCoffeeZoneDto(id, "Center", null, 53.9m, 27.56m, 400, 3, [], "#A1B2C3");

        var result = await service.MapAsync([], [], [zone], false, CancellationToken.None);

        Assert.Single(result.Zones);
        Assert.Equal(zone.Color, result.Zones[0].Color);
    }
}
