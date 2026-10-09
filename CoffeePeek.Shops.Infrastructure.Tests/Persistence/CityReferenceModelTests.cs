using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CityReferenceModelTests
{
    [Fact]
    public void ShopRoasterAndZoneReferencesUseRestrictiveCityForeignKeys()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=shops;Username=postgres;Password=postgres")
            .Options);
        var model = db.Model;

        AssertCityForeignKey(model.FindEntityType(typeof(CoffeeShop))!
            .FindNavigation(nameof(CoffeeShop.Location))!.TargetEntityType);
        AssertCityForeignKey(model.FindEntityType(typeof(Roaster))!
            .FindNavigation(nameof(Roaster.Location))!.TargetEntityType);
        AssertCityForeignKey(model.FindEntityType(typeof(CoffeeZone))!);
    }

    private static void AssertCityForeignKey(IEntityType entityType)
    {
        var foreignKey = Assert.Single(entityType.GetForeignKeys(), fk =>
            fk.PrincipalEntityType.ClrType == typeof(City)
            && fk.Properties.Single().Name == nameof(Location.CityId));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }
}
