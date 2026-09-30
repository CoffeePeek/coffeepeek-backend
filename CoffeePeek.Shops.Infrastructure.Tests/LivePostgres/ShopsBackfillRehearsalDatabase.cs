using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed class ShopsBackfillRehearsalDatabase : ShopsLiveDatabase
{
    protected override string Service => "shopsbackfill";
    protected override string PreFeatureMigration => "20260923120247_AddCoffeeZonePolygon";
    public string[] BeforeMigrations { get; private set; } = [];
    public bool RegistryExistedBefore { get; private set; }
    public Guid[] CityIds { get; private set; } = [];
    public Guid[] ShopIds { get; private set; } = [];
    public Guid[] RoasterIds { get; private set; } = [];
    public Guid[] ZoneIds { get; private set; } = [];
    public Guid HiddenShopId { get; private set; }
    public Guid DraftZoneId { get; private set; }

    protected override async Task SeedLegacyAsync(ShopsDbContext db, CancellationToken ct)
    {
        BeforeMigrations = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        await using (var connection = await CoffeePeek.Testing.LivePostgresServer.OpenAsync(ConnectionString, ct))
        await using (var command = new NpgsqlCommand("SELECT to_regclass('public.\"CoffeeShopPublicAddresses\"') IS NOT NULL", connection))
            RegistryExistedBefore = (bool)(await command.ExecuteScalarAsync(ct))!;
        var cities = new[] { new City("Минск"), new City("Минск"), new City("Брест") };
        db.Cities.AddRange(cities);
        var shops = new[] { NewShop("Legacy cafe", cities[0].Id), NewShop("Legacy cafe", cities[1].Id), NewShop("Legacy third", cities[0].Id) };
        shops[2].SetHidden(true);
        db.Shops.AddRange(shops);
        var roasters = new[] { new Roaster("Legacy roaster"), new Roaster("Legacy roaster"), new Roaster("Legacy other") };
        db.Roasters.AddRange(roasters);
        var zones = new[] { NewZone(cities[0].Id, "Legacy zone"), NewZone(cities[1].Id, "Legacy zone") };
        foreach (var zone in zones) zone.Publish();
        var draft = NewZone(cities[0].Id, "Legacy draft");
        var archived = NewZone(cities[0].Id, "Legacy archived");
        archived.Publish();
        archived.Archive();
        db.CoffeeZones.AddRange(zones.Append(draft).Append(archived));
        await db.SaveChangesAsync(ct);
        CityIds = cities.Select(x => x.Id).ToArray();
        ShopIds = shops.Select(x => x.Id).ToArray();
        RoasterIds = roasters.Select(x => x.Id).ToArray();
        ZoneIds = zones.Select(x => x.Id).ToArray();
        HiddenShopId = shops[2].Id;
        DraftZoneId = draft.Id;
    }
}
