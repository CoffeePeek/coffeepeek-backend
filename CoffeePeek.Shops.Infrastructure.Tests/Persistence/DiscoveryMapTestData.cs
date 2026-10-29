using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.Extensions.Options;
using Moq;
using PriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

internal sealed class DiscoveryMapTestData
{
    public Guid UserId { get; } = Guid.NewGuid();
    public City City { get; } = NewCity();
    public CoffeeShop Matching { get; private set; } = null!;
    public CoffeeShop Other { get; private set; } = null!;
    private string roasterSlug = "";
    private string equipmentSlug = "";
    private string beanSlug = "";
    private string methodSlug = "";
    private string[] tagSlugs = [];

    public static string Key() => "map-" + Guid.NewGuid().ToString("N");

    public static DiscoveryQueryStore Store(ShopsDbContext db, int limit = 500) => new(db,
        Mock.Of<IPublicAddressService>(),
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }),
        Options.Create(new MapClusteringOptions { MaxResponseItems = limit }));

    public static CoffeeShopMapRequest Request(ShopFilters? filters = null, int zoom = 16, string q = "") => new()
    {
        Q = q, Filters = filters, Zoom = zoom,
        Bounds = new() { MinLat = 53.8m, MinLon = 27.4m, MaxLat = 54m, MaxLon = 27.7m }
    };

    public static City NewCity()
    {
        var city = new City(Key());
        city.SetPublicSlug(Key());
        return city;
    }

    public static CoffeeShop Shop(ShopsDbContext db, City city, string name, decimal latitude = 53.9m,
        decimal longitude = 27.56m, bool publicSlug = true)
    {
        var shop = new CoffeeShop(null, name, null, PriceRange.Moderate, Guid.NewGuid());
        shop.CreatedAtUtc = DateTime.UtcNow;
        shop.SetLocation(city.Id, "Street", latitude, longitude);
        if (publicSlug) shop.SetPublicSlug(Key());
        db.Shops.Add(shop);
        return shop;
    }

    public static async Task<DiscoveryMapTestData> SeedAsync(ShopsDbContext db, CancellationToken ct)
    {
        var data = new DiscoveryMapTestData();
        db.Cities.Add(data.City);
        data.Matching = Shop(db, data.City, "Ёлка Coffeehouse");
        data.Other = Shop(db, data.City, "Plain", 53.92m);
        data.Matching.SetPriceRange(PriceRange.Cheap);
        data.Matching.SetCoffeeFocus(CoffeeFocusType.Specialty);
        data.Other.SetCoffeeFocus(CoffeeFocusType.Cafe);
        AddSchedules(data.Matching, false);
        AddSchedules(data.Other, true);
        await data.AddReferencesAsync(db, ct);
        data.AddMenu(db);
        data.AddPersonalData(db);
        await db.SaveChangesAsync(ct);
        // The live harness stamps Added entities. Backdate only after the insert.
        data.Other.CreatedAtUtc = DateTime.UtcNow.AddYears(-1);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return data;
    }

    public IEnumerable<(string Name, ShopFilters Filters, CoffeeShop[] Expected)> Cases()
    {
        yield return ("city", new() { City = City.PublicSlug }, [Matching, Other]);
        yield return ("all tags (AND)", new() { Tags = tagSlugs }, [Matching]);
        yield return ("roaster", new() { Roasters = [roasterSlug] }, [Matching]);
        yield return ("equipment", new() { Equipments = [equipmentSlug] }, [Matching]);
        yield return ("beans", new() { Beans = [beanSlug] }, [Matching]);
        yield return ("brew methods", new() { BrewMethods = [methodSlug] }, [Matching]);
        yield return ("price", new() { PriceRange = "cheap" }, [Matching]);
        yield return ("type", new() { Type = "specialty" }, [Matching]);
        yield return ("open", new() { IsOpen = true }, [Matching]);
        yield return ("closed", new() { IsOpen = false }, [Other]);
        yield return ("new", new() { IsNew = true }, [Matching]);
        yield return ("not new", new() { IsNew = false }, [Other]);
        yield return ("rating", new() { MinRating = 4 }, [Matching]);
        yield return ("visited", new() { VisitedOnly = true }, [Matching]);
        yield return ("favorites", new() { FavoritesOnly = true }, [Matching]);
        yield return ("nearby", new() { Origin = new() { Latitude = 53.9m, Longitude = 27.56m }, RadiusKm = 0.5m }, [Matching]);
        yield return ("menu", new() { Menu = new() { Brew = ["filter"], Currency = "BYN", MinPrice = 4, MaxPrice = 6 } }, [Matching]);
        yield return ("combined", new()
        {
            Tags = tagSlugs, Roasters = [roasterSlug], Equipments = [equipmentSlug], Beans = [beanSlug],
            BrewMethods = [methodSlug], PriceRange = "cheap", Type = "specialty", IsOpen = true, IsNew = true,
            MinRating = 4, VisitedOnly = true, FavoritesOnly = true,
            Origin = new() { Latitude = 53.9m, Longitude = 27.56m }, RadiusKm = 0.5m,
            Menu = new() { Brew = ["filter"], Currency = "BYN", MinPrice = 4, MaxPrice = 6 }
        }, [Matching]);
        yield return ("incompatible filters intersect", new() { PriceRange = "cheap", Type = "cafe" }, []);
    }

    public static async Task AssertParityAsync(DiscoveryQueryStore store, ShopFilters filters,
        Guid? userId, CoffeeShop[] expected, CancellationToken ct, string q = "")
    {
        var list = await store.SearchShopsAsync(new() { Q = q, Filters = filters, PageSize = 100 }, userId, ct);
        var map = await store.MapShopsAsync(Request(filters, q: q), userId, ct);
        Assert.Equal(expected.Select(s => s.PublicSlug).Order(), list.Items.Select(s => s.Address.Slug).Order());
        Assert.Equal(expected.Select(s => s.Id).Order(), map.Shops.Select(s => s.Id).Order());
        Assert.Equal(expected.Length, list.TotalItems);
        Assert.False(map.IsTruncated);
    }

    private async Task AddReferencesAsync(ShopsDbContext db, CancellationToken ct)
    {
        var roaster = new Roaster(Key()); roaster.SetPublicSlug(Key());
        var category = new EquipmentCategory();
        db.EquipmentCategories.Add(category);
        db.Entry(category).Property(nameof(EquipmentCategory.Name)).CurrentValue = Key();
        await db.SaveChangesAsync(ct);
        var equipment = new Equipment("Maker", "Model", category); equipment.SetPublicSlug(Key());
        var bean = new CoffeeBean(Key()); bean.SetPublicSlug(Key());
        var method = new BrewMethod("V60", BrewMethodCategory.Gravity); method.SetPublicSlug(Key());
        var first = ShopTag.Create(Key(), "Laptop friendly");
        var second = ShopTag.Create(Key(), "Pet friendly");
        db.AddRange(roaster, equipment, bean, method, first, second);
        Matching.SetRoasters([roaster]); Matching.SetEquipment([equipment]);
        Matching.SetBeans([bean]); Matching.SetBrewMethods([method]);
        Matching.SetTags([first.Id, second.Id], null); Other.SetTags([first.Id], null);
        roasterSlug = roaster.PublicSlug!; equipmentSlug = equipment.PublicSlug!;
        beanSlug = bean.PublicSlug!; methodSlug = method.PublicSlug!;
        tagSlugs = [first.Slug, second.Slug];
    }

    private void AddMenu(ShopsDbContext db)
    {
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), Key(), "Фильтр", "Filter",
            CoffeeDrinkCategory.Filter, "", 10);
        var menu = ShopMenu.Create(Matching.Id);
        menu.ApplyManualItem(drink.Id, MenuItemAvailability.Present, 5, null);
        db.AddRange(drink, menu);
    }

    private void AddPersonalData(ShopsDbContext db)
    {
        var checkIn = CheckIn.CreateUnified(UserId, Matching.Id, "author", "visit",
            Rating.Create(5, 5, 5), DateTime.UtcNow, CheckInVisibility.Public);
        checkIn.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow);
        db.CheckIns.Add(checkIn);
        db.DiscoveryFavorites.Add(new(UserId, "coffee_shop", Matching.Id));
    }

    private static void AddSchedules(CoffeeShop shop, bool closed) => shop.AddSchedule(
        Enum.GetValues<DayOfWeek>().Select(day => ShopSchedule.Create(day, closed,
            closed ? [] : [new(TimeSpan.Zero, TimeSpan.FromDays(1) - TimeSpan.FromTicks(10))])).ToList());
}
