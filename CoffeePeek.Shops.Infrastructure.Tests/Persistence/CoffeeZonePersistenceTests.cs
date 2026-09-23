using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CoffeeZonePersistenceTests
{
    [Fact]
    public void Model_EnforcesZoneBoundsAndSingleExplicitPrimary()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options;

        using var dbContext = new ShopsDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var zone = model.FindEntityType(typeof(CoffeeZone));
        var membershipOverride = model.FindEntityType(typeof(CoffeeZoneMembershipOverride));

        zone.Should().NotBeNull();
        zone!.GetCheckConstraints().Select(c => c.Name).Should().Contain(
            "CK_CoffeeZones_Latitude",
            "CK_CoffeeZones_Longitude",
            "CK_CoffeeZones_RadiusMeters",
            "CK_CoffeeZones_Status");
        var cityStatusIndexes = zone.GetIndexes()
            .Count(index => index.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(CoffeeZone.CityId), nameof(CoffeeZone.Status) }));
        cityStatusIndexes.Should().Be(1);

        membershipOverride.Should().NotBeNull();
        membershipOverride!.GetCheckConstraints().Select(c => c.Name)
            .Should().Contain("CK_CoffeeZoneMembershipOverrides_Kind");
        var primaryIndex = membershipOverride.GetIndexes().Single(index => index.IsUnique);
        primaryIndex.Properties.Should().ContainSingle(p => p.Name == nameof(CoffeeZoneMembershipOverride.ShopId));
        primaryIndex.GetFilter().Should().Be("\"Kind\" = 2");
    }
}
