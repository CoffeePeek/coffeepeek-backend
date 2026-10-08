using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CityRepositoryTests
{
    [Theory]
    [InlineData("shop")]
    [InlineData("roaster")]
    [InlineData("zone")]
    public async Task ReferencedCityCannotBeDeleted(string reference)
    {
        await using var db = NewContext();
        var city = new City("Referenced city " + reference);
        db.Cities.Add(city);
        switch (reference)
        {
            case "shop":
                db.Shops.Add(ShopsLiveDatabase.NewShop("City shop", city.Id));
                break;
            case "roaster":
                var roaster = new Roaster("City roaster");
                roaster.SetLocation(Location.CreateDraft(city.Id, "Address"));
                db.Roasters.Add(roaster);
                break;
            case "zone":
                db.CoffeeZones.Add(new CoffeeZone(city.Id, "City zone", null,
                    CoffeeZonePoint.Circle(53.9m, 27.56m, 400)));
                break;
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await new CityRepository(db).DeleteIfUnusedAsync(city.Id, TestContext.Current.CancellationToken);

        Assert.Equal(CityDeleteResult.Referenced, result);
        Assert.True(await db.Cities.AnyAsync(c => c.Id == city.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnusedCityIsDeleted()
    {
        await using var db = NewContext();
        var city = new City("Unused city");
        db.Cities.Add(city);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new CityRepository(db).DeleteIfUnusedAsync(city.Id, TestContext.Current.CancellationToken);

        Assert.Equal(CityDeleteResult.Deleted, result);
        Assert.False(await db.Cities.AnyAsync(c => c.Id == city.Id, TestContext.Current.CancellationToken));
    }

    private static ShopsDbContext NewContext() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase("city-reference-" + Guid.NewGuid())
        .Options);
}
