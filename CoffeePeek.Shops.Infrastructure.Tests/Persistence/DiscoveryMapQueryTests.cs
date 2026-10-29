using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using static CoffeePeek.Shops.Infrastructure.Tests.Persistence.DiscoveryMapTestData;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class DiscoveryMapQueryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task EveryShopFilter_SelectsTheSameEligibleShopsForListAndMarkers()
    {
        await using var db = Database();
        var data = await SeedAsync(db, Ct);
        var otherCity = NewCity(); db.Cities.Add(otherCity);
        Shop(db, otherCity, "Other city");
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        foreach (var (name, filters, expected) in data.Cases())
        {
            filters.City = data.City.PublicSlug;
            try { await AssertParityAsync(store, filters, data.UserId, expected, Ct); }
            catch (Exception e) { throw new InvalidOperationException($"Filter parity failed: {name}", e); }
        }
    }

    [Fact]
    public async Task HiddenClosedAndUnpublishedShops_AreExcludedBeforeEveryMapRepresentation()
    {
        await using var db = Database();
        var city = NewCity(); db.Cities.Add(city);
        var expected = Shop(db, city, "Visible");
        Shop(db, city, "Hidden").SetHidden(true);
        Shop(db, city, "Closed").SetStatus(CoffeeShopStatus.PermanentlyClosed);
        Shop(db, city, "No public slug", publicSlug: false);
        await db.SaveChangesAsync(Ct);
        await AssertParityAsync(Store(db), new(), null, [expected], Ct);
        var clusters = await Store(db).MapShopsAsync(Request(zoom: 5), null, Ct);
        Assert.Equal(1, clusters.Clusters!.Sum(c => c.Count));
    }

    [Fact]
    public async Task NearbyUsesUserOrigin_AndIntersectsRatherThanReplacesTheViewport()
    {
        await using var db = Database();
        var city = NewCity(); db.Cities.Add(city);
        var inside = Shop(db, city, "Inside", 53.9001m);
        var outsideViewport = Shop(db, city, "Outside viewport", 53.898m);
        Shop(db, city, "Outside radius", 53.91m);
        await db.SaveChangesAsync(Ct);
        var filters = new ShopFilters { Origin = new() { Latitude = 53.9m, Longitude = 27.56m }, RadiusKm = 0.5m };
        var list = await Store(db).SearchShopsAsync(new() { Filters = filters }, null, Ct);
        Assert.Equal(new[] { inside.PublicSlug, outsideViewport.PublicSlug }.Order(), list.Items.Select(i => i.Address.Slug).Order());
        var request = Request(filters);
        request.Bounds.MinLat = 53.9m;
        var map = await Store(db).MapShopsAsync(request, null, Ct);
        Assert.Equal(inside.Id, Assert.Single(map.Shops).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersonalFilters_RequireIdentityAndIsolateEachUsersShops(bool favorites)
    {
        await using var db = Database();
        var data = await SeedAsync(db, Ct);
        var otherUser = Guid.NewGuid();
        db.DiscoveryFavorites.Add(new(otherUser, "coffee_shop", data.Other.Id));
        db.CheckIns.Add(CheckIn.CreateUnified(otherUser, data.Other.Id, "author", "visit",
            Rating.Create(3, 3, 3), DateTime.UtcNow, CheckInVisibility.Private));
        var deletedVisit = CheckIn.CreateUnified(otherUser, data.Matching.Id, "author", "deleted visit",
            Rating.Create(3, 3, 3), DateTime.UtcNow, CheckInVisibility.Private);
        deletedVisit.Delete(); db.CheckIns.Add(deletedVisit);
        await db.SaveChangesAsync(Ct);
        var filters = new ShopFilters { FavoritesOnly = favorites, VisitedOnly = !favorites };
        var store = Store(db);
        var error = await Assert.ThrowsAsync<DiscoveryException>(() => store.MapShopsAsync(Request(filters), null, Ct));
        Assert.Equal(401, error.StatusCode);
        await AssertParityAsync(store, filters, data.UserId, [data.Matching], Ct);
        await AssertParityAsync(store, filters, otherUser, [data.Other], Ct);
        await AssertParityAsync(store, filters, Guid.NewGuid(), [], Ct);
    }

    [Fact]
    public async Task MarkerLimitAndTruncation_AreAppliedAfterFilters()
    {
        await using var db = Database();
        var city = NewCity(); db.Cities.Add(city);
        var matching = Enumerable.Range(0, 3).Select(i => Shop(db, city, $"Match {i}")).ToArray();
        for (var i = 0; i < 5; i++) Shop(db, city, $"Other {i}");
        await db.SaveChangesAsync(Ct);
        var store = Store(db, limit: 2);
        var map = await store.MapShopsAsync(Request(q: "Match"), null, Ct);
        Assert.True(map.IsTruncated);
        Assert.Equal(matching.Select(s => s.Id).Order().Take(2), map.Shops.Select(s => s.Id));
        var single = await store.MapShopsAsync(Request(q: "Match 1"), null, Ct);
        Assert.Equal(matching[1].Id, Assert.Single(single.Shops).Id);
        Assert.False(single.IsTruncated);
        var clusters = await store.MapShopsAsync(Request(zoom: 5, q: "Match"), null, Ct);
        Assert.Equal(3, Assert.Single(clusters.Clusters!).Count);
        Assert.False(clusters.IsTruncated);
    }

    [Theory]
    [InlineData("city")]
    [InlineData("tags")]
    [InlineData("roasters")]
    [InlineData("equipments")]
    [InlineData("beans")]
    [InlineData("brewMethods")]
    public async Task UnknownPublicFilterSlugs_AreRejectedByBothQueries(string field)
    {
        await using var db = Database();
        var filters = field switch
        {
            "city" => new ShopFilters { City = "missing" },
            "tags" => new ShopFilters { Tags = ["missing"] },
            "roasters" => new ShopFilters { Roasters = ["missing"] },
            "equipments" => new ShopFilters { Equipments = ["missing"] },
            "beans" => new ShopFilters { Beans = ["missing"] },
            _ => new ShopFilters { BrewMethods = ["missing"] }
        };
        var store = Store(db);
        var list = await Assert.ThrowsAsync<DiscoveryException>(() => store.SearchShopsAsync(new() { Filters = filters }, null, Ct));
        var map = await Assert.ThrowsAsync<DiscoveryException>(() => store.MapShopsAsync(Request(filters), null, Ct));
        Assert.Equal(400, map.StatusCode);
        Assert.Equal(list.ErrorCode, map.ErrorCode);
    }
}
