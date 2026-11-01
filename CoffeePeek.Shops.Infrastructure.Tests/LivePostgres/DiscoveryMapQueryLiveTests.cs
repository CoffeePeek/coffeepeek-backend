using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Testing;
using static CoffeePeek.Shops.Infrastructure.Tests.Persistence.DiscoveryMapTestData;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class DiscoveryMapQueryLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DiscoveryMapQueryLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task AllFilters_SelectTheSameListMarkersAndZoneMembers_InPostgres()
    {
        await using var db = fixture.Context(false);
        var data = await SeedAsync(db, Ct);
        var otherCity = NewCity(); db.Cities.Add(otherCity);
        Shop(db, otherCity, "Other city");
        var zone = new CoffeeZone(data.City.Id, Key(), null, CoffeeZonePoint.Circle(53.91m, 27.56m, 1500));
        zone.SetPublicSlug(Key()); zone.Publish(); db.CoffeeZones.Add(zone);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        foreach (var (name, filters, expected) in data.Cases())
        {
            filters.City = data.City.PublicSlug;
            try
            {
                await AssertParityAsync(store, filters, data.UserId, expected, Ct);
                var zones = await store.MapShopsAsync(Request(filters, 12), data.UserId, Ct);
                if (expected.Length == 0) Assert.Empty(zones.Zones!);
                else Assert.Equal(expected.Length, Assert.Single(zones.Zones!).ShopCount);
            }
            catch (Exception e) { throw new InvalidOperationException($"PostgreSQL map filter failed: {name}", e); }
        }
    }

    [Theory]
    [InlineData("  ЕЛКА  ", "Ёлка Coffeehouse", null)]
    [InlineData("  ЕЛКА  ", "Different name", "Ёлка street")]
    [InlineData("Coffehouse", "Coffeehouse", null)]
    [InlineData("  КОФЕЁКК  ", "Кофеёк", null)]
    public async Task NormalizedAndFuzzyQueries_HaveIdenticalListAndMapMembership_InPostgres(string query, string name, string? address)
    {
        await using var db = fixture.Context(false);
        var city = NewCity(); db.Cities.Add(city);
        var expected = Shop(db, city, name);
        if (address is not null) expected.SetLocation(city.Id, address, 53.9m, 27.56m);
        Shop(db, city, name).SetHidden(true);
        Shop(db, city, "Unrelated");
        await db.SaveChangesAsync(Ct);
        await AssertParityAsync(Store(db), new() { City = city.PublicSlug }, null, [expected], Ct, query);
    }

    [Fact]
    public async Task ExactMatchOutsideViewport_PreventsDifferentFuzzyFallbackInsideViewport_InPostgres()
    {
        await using var db = fixture.Context(false);
        var city = NewCity(); db.Cities.Add(city);
        Shop(db, city, "Starbucks");
        var exact = Shop(db, city, "Starbuks", 54.1m);
        await db.SaveChangesAsync(Ct);
        var filters = new ShopFilters { City = city.PublicSlug };
        var store = Store(db);
        var list = await store.SearchShopsAsync(new() { Q = "Starbuks", Filters = filters }, null, Ct);
        Assert.Equal(exact.PublicSlug, Assert.Single(list.Items).Address.Slug);
        var map = await store.MapShopsAsync(Request(filters, q: "Starbuks"), null, Ct);
        Assert.Empty(map.Shops);
        Assert.False(map.IsTruncated);
    }
    [Fact]
    public async Task ClustersAndZones_CountOnlyMatchingShops_AndPreserveMembershipOverrides()
    {
        await using var db = fixture.Context(false);
        var city = NewCity(); db.Cities.Add(city);
        var member = Shop(db, city, "Match member");
        var excluded = Shop(db, city, "Match excluded", 53.9001m);
        var included = Shop(db, city, "Match included", 53.92m);
        var includedByOverride = Shop(db, city, "Match override", 53.93m);
        Shop(db, city, "Other");
        var zone = new CoffeeZone(city.Id, "Zone", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
        zone.SetPublicSlug(Key()); zone.Publish(); db.CoffeeZones.Add(zone);
        db.CoffeeZoneMembershipOverrides.AddRange(
            new(zone.Id, excluded.Id, CoffeeZoneMembershipOverrideKind.Exclude),
            new(zone.Id, included.Id, CoffeeZoneMembershipOverrideKind.Primary),
            new(zone.Id, includedByOverride.Id, CoffeeZoneMembershipOverrideKind.Include));
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var clusters = await store.MapShopsAsync(Request(zoom: 5, q: "Match"), null, Ct);
        Assert.Equal(4, clusters.Clusters!.Sum(c => c.Count));
        var zoneRequest = Request(zoom: 12, q: "Match");
        zoneRequest.Bounds.MaxLat = 53.901m;
        var zones = await store.MapShopsAsync(zoneRequest, null, Ct);
        Assert.Equal(3, Assert.Single(zones.Zones!).ShopCount);
        Assert.Equal(1, zones.Clusters!.Sum(c => c.Count));
        var markers = await store.MapShopsAsync(Request(q: "Match"), null, Ct);
        Assert.Equal(zone.Id, markers.Shops.Single(s => s.Id == member.Id).PrimaryZoneId);
        Assert.Equal(zone.Id, markers.Shops.Single(s => s.Id == included.Id).PrimaryZoneId);
        Assert.Equal(zone.Id, markers.Shops.Single(s => s.Id == includedByOverride.Id).PrimaryZoneId);
        Assert.Null(markers.Shops.Single(s => s.Id == excluded.Id).PrimaryZoneId);
        var limited = await Store(db, limit: 1).MapShopsAsync(zoneRequest, null, Ct);
        Assert.Single(limited.Zones!); Assert.Empty(limited.Clusters!); Assert.True(limited.IsTruncated);
        var empty = await store.MapShopsAsync(Request(new() { PriceRange = "expensive" }, 12), null, Ct);
        Assert.Empty(empty.Zones!); Assert.Empty(empty.Clusters!);
    }

}
