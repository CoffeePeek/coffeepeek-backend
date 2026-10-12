using System;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Application.Tests.PublicAddresses;

public sealed class PublicAddressOwnerWriterTests
{
    [Fact]
    public void AllOwnerModulesRejectStaleRevisionBeforeMutationAndSameSlugIsIdempotent()
    {
        IPublicSlugOwner[] owners =
        [
            new CoffeeShop(null, "Shop", null, PriceRange.Moderate, Guid.NewGuid()),
            new City("City"), new CoffeeZone(Guid.NewGuid(), "Zone", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400)),
            new CoffeeBean("Bean"), new Equipment("Brand", "Model", new EquipmentCategory()),
            new BrewMethod("Pour over", BrewMethodCategory.Gravity), ShopTag.Create("wifi", "WiFi"),
            new Roaster("Roaster"), new CoffeeProduct(Guid.NewGuid(), new CoffeeContent("Coffee", null,
                "roasted_beans", "whole_beans", "unknown", null, null, null, null, null, [], [], [], []))
        ];
        var writer = new PublicAddressOwnerWriter();
        foreach (var owner in owners)
        {
            writer.Apply(owner, "first", 0);
            Assert.Equal("first", owner.PublicSlug);
            Assert.Equal(1, owner.SlugRevision);
            Assert.Equal(409, Assert.Throws<PublicAddressException>(() => writer.Apply(owner, "second", 0)).StatusCode);
            Assert.Equal("first", owner.PublicSlug);
            Assert.Equal(1, owner.SlugRevision);
            writer.Apply(owner, "first", 1);
            Assert.Equal(1, owner.SlugRevision);
            writer.Apply(owner, "second", 1);
            Assert.Equal("second", owner.PublicSlug);
            Assert.Equal(2, owner.SlugRevision);
        }
    }

    [Fact]
    public void ModuleWritersCannotChangeOtherModulesOwner()
    {
        var roaster = new Roaster("Roaster");
        var city = new City("City");
        Assert.Throws<PublicAddressException>(() => ShopPublicAddressWriter.Apply(roaster, "wrong"));
        Assert.Throws<PublicAddressException>(() => CoffeeCatalogPublicAddressWriter.Apply(city, "wrong"));
        Assert.Null(roaster.PublicSlug);
        Assert.Null(city.PublicSlug);
    }
}
