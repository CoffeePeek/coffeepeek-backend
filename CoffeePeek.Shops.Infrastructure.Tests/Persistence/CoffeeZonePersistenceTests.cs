using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
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
        dbContext.Database.HasPendingModelChanges().Should().BeFalse();
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var zone = model.FindEntityType(typeof(CoffeeZone));
        var membershipOverride = model.FindEntityType(typeof(CoffeeZoneMembershipOverride));

        zone.Should().NotBeNull();
        zone!.FindComplexProperty(nameof(CoffeeZone.Polygon))!.FieldInfo!.Name.Should().Be("_polygon");
        zone!.GetCheckConstraints().Select(c => c.Name).Should().Contain(
            "CK_CoffeeZones_Latitude",
            "CK_CoffeeZones_Longitude",
            "CK_CoffeeZones_RadiusMeters",
            "CK_CoffeeZones_Status",
            "CK_CoffeeZones_Color");
        var color = zone.FindProperty(nameof(CoffeeZone.Color));
        color.Should().NotBeNull();
        color!.GetMaxLength().Should().Be(7);
        color.GetDefaultValue().Should().Be(CoffeeZone.DefaultColor);
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

    [Fact]
    public async Task Polygon_DetachedViewDoesNotBecomeTrackedChange()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase("zone-polygon-" + Guid.NewGuid())
            .Options;
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Zone city");
        var initial = CoffeeZonePoint.Circle(53.9m, 27.56m, 400);
        var replacement = CoffeeZonePoint.Circle(53.91m, 27.57m, 500);

        await using (var db = new ShopsDbContext(options))
        {
            var zone = new CoffeeZone(city.Id, "Center", null, initial);
            db.Cities.Add(city);
            db.CoffeeZones.Add(zone);
            await db.SaveChangesAsync(ct);

            zone.Update(city.Id, "Center", null, replacement);
            await db.SaveChangesAsync(ct);
            var leaked = zone.Polygon;
            leaked.Clear();
            db.ChangeTracker.DetectChanges();
            db.Entry(zone).State.Should().Be(EntityState.Unchanged);
            await db.SaveChangesAsync(ct);
            zone.Polygon.Should().Equal(replacement);
            zone.Contains(53.91m, 27.57m).Should().BeTrue();
        }
    }
}
