using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public class ShopsLiveDatabase : LiveDatabase<ShopsDbContext>
{
    protected override string Service => "shops";
    protected override ShopsDbContext Create(DbContextOptions<ShopsDbContext> options, bool enabled) =>
        new(options, Options.Create(new PublicAddressOptions { Enabled = enabled }));

    public static CoffeeShop NewShop(string name, Guid cityId)
    {
        var shop = new CoffeeShop(Guid.NewGuid(), name, null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetDraftLocation(cityId, name + " address");
        return shop;
    }

    public static CoffeeZone NewZone(Guid cityId, string name) =>
        new(cityId, name, null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));

    public async Task<Guid> AddOwnerAsync(PublicAddressKind kind, string name, bool enabled, CancellationToken ct)
    {
        await using var db = Context(enabled);
        object owner = kind switch
        {
            PublicAddressKind.CoffeeShop => Shop(),
            PublicAddressKind.Roaster => new Roaster(name),
            PublicAddressKind.City => new City(name),
            PublicAddressKind.CoffeeZone => await ZoneAsync(),
            PublicAddressKind.CoffeeBean => new CoffeeBean(name),
            PublicAddressKind.Equipment => Equipment(),
            PublicAddressKind.BrewMethod => new BrewMethod(name, BrewMethodCategory.Gravity),
            PublicAddressKind.ShopTag => ShopTag.Create(PublicSlug.Generate(name), name),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        db.Add(owner);
        await db.SaveChangesAsync(ct);
        return (Guid)db.Entry(owner).Property("Id").CurrentValue!;

        Equipment Equipment()
        {
            var category = new EquipmentCategory();
            db.Add(category);
            db.Entry(category).Property(nameof(EquipmentCategory.Name)).CurrentValue = "live category";
            return new Equipment(name, "model", category);
        }

        CoffeeShop Shop()
        {
            var city = new City(name + " city");
            db.Cities.Add(city);
            return NewShop(name, city.Id);
        }

        async Task<CoffeeZone> ZoneAsync()
        {
            var city = new City(name + " city");
            db.Cities.Add(city);
            var zone = NewZone(city.Id, name);
            zone.Publish();
            return zone;
        }
    }

}
