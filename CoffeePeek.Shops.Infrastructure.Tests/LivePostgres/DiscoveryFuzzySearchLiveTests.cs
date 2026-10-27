using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class DiscoveryFuzzySearchLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public DiscoveryFuzzySearchLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    private ShopsDbContext Context(ReadPerformanceCommandProbe probe) => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseNpgsql(fixture.ConnectionString).AddInterceptors(probe).Options);
    private static DiscoveryQueryStore Store(ShopsDbContext db) => new(db, Mock.Of<IPublicAddressService>(),
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }));
    private static string Key() => "fuzzy-" + Guid.NewGuid().ToString("N");
    private static City City(ShopsDbContext db)
    {
        var city = new City(Key()); city.SetPublicSlug(Key()); db.Cities.Add(city); return city;
    }
    private static CoffeeShop Shop(ShopsDbContext db, City city, string name, string? slug = null)
    {
        var shop = new CoffeeShop(null, name, null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetPublicSlug(slug ?? Key()); shop.SetLocation(city.Id, "Street", 53.9m, 27.56m);
        db.Shops.Add(shop); return shop;
    }

    [Theory]
    [InlineData("Starbuks", "Starbucks")]
    [InlineData("Strabucks", "Starbucks")]
    [InlineData("  КОФЕЁКК  ", "Кофеёк")]
    [InlineData("Starbuks", "A very long unrelated prefix with many different words Starbucks specialty coffee house")]
    public async Task TyposMatchNormalizedNamesIncludingWordsInLongNames(string query, string name)
    {
        await using var db = fixture.Context(false);
        var city = City(db);
        var expected = Shop(db, city, name);
        Shop(db, city, name).SetHidden(true);
        Shop(db, city, name).SetStatus(CoffeeShopStatus.PermanentlyClosed);
        Shop(db, city, "Other").SetLocation(city.Id, name, 53.9m, 27.56m);
        await db.SaveChangesAsync(Ct);
        var page = await Store(db).SearchShopsAsync(new() { Q = query, Filters = new() { City = city.PublicSlug } }, null, Ct);
        Assert.Equal(1, page.TotalItems);
        Assert.Equal(expected.PublicSlug, Assert.Single(page.Items).Address.Slug);
    }

    [Theory]
    [InlineData("Starbucks", 1)]
    [InlineData("Starbucks", 99)]
    [InlineData("Street", 1)]
    [InlineData("abc", 1)]
    [InlineData("123456", 1)]
    [InlineData("ab123456", 1)]
    [InlineData("", 1)]
    public async Task ExactMatchesEmptyPagesAndShortOrNumericQueriesDoNotRunFuzzy(string query, int pageNumber)
    {
        await using var seed = fixture.Context(false);
        var city = City(seed); Shop(seed, city, "Starbucks");
        await seed.SaveChangesAsync(Ct);
        using var probe = new ReadPerformanceCommandProbe(capturePlans: true);
        await using var db = Context(probe);
        var page = await Store(db).SearchShopsAsync(new()
            { Q = query, Page = pageNumber, PageSize = 1, Filters = new() { City = city.PublicSlug } }, null, Ct);
        var hasMatch = query is "Starbucks" or "Street" or "";
        Assert.Equal(hasMatch ? 1 : 0, page.TotalItems);
        Assert.Equal(hasMatch && pageNumber == 1 ? 1 : 0, page.Items.Length);
        Assert.DoesNotContain(probe.Commands, c => c.CommandText.Contains("word_similarity"));
        Assert.Equal(pageNumber, page.CurrentPage); Assert.Equal(1, page.PageSize);
    }

    [Fact]
    public async Task FuzzyRelevanceOutranksAlphabeticalOrderBeforePaging()
    {
        await using var db = fixture.Context(false);
        var city = City(db);
        var closer = Shop(db, city, "Z Coffeehouse");
        var weaker = Shop(db, city, "A Coffeehousa");
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var request = new CoffeeShopSearchRequest { Q = "Coffehouse", PageSize = 1, Filters = new() { City = city.PublicSlug } };
        var first = await store.SearchShopsAsync(request, null, Ct);
        Assert.Equal(2, first.TotalItems); Assert.Equal(closer.PublicSlug, Assert.Single(first.Items).Address.Slug);
        request.Page = 2;
        Assert.Equal(weaker.PublicSlug, Assert.Single((await store.SearchShopsAsync(request, null, Ct)).Items).Address.Slug);
        request.Page = 1; request.Sort = "name_asc";
        Assert.Equal(weaker.PublicSlug, Assert.Single((await store.SearchShopsAsync(request, null, Ct)).Items).Address.Slug);
    }

    [Fact]
    public async Task FuzzyCandidatesAreCappedAfterFiltersAndHaveStablePages()
    {
        await using var db = fixture.Context(false);
        var city = City(db); var otherCity = City(db);
        for (var i = 0; i < 205; i++)
        {
            Shop(db, city, "Starbucks", $"{city.PublicSlug}-{i:D3}");
            Shop(db, otherCity, "Starbuks");
            Shop(db, city, "Starbuks").SetHidden(true);
        }
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var request = new CoffeeShopSearchRequest { Q = "Starbuks", PageSize = 100, Filters = new() { City = city.PublicSlug } };
        var first = await store.SearchShopsAsync(request, null, Ct);
        request.Page = 2; var second = await store.SearchShopsAsync(request, null, Ct);
        request.Page = 3; var empty = await store.SearchShopsAsync(request, null, Ct);
        Assert.Equal(200, first.TotalItems); Assert.Equal(2, first.TotalPages);
        Assert.Equal(200, second.TotalItems); Assert.Equal(200, empty.TotalItems); Assert.Empty(empty.Items);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => $"{city.PublicSlug}-{i:D3}"),
            first.Items.Concat(second.Items).Select(i => i.Address.Slug));
    }

    [Fact]
    public async Task FuzzyHonorsPersonalRatingAndRadiusFiltersAndExplicitSorts()
    {
        await using var db = fixture.Context(false);
        var city = City(db); var user = Guid.NewGuid();
        var near = Shop(db, city, "Starbucks Near");
        var far = Shop(db, city, "Starbucks Far"); far.SetLocation(city.Id, "Street", 53.91m, 27.56m);
        foreach (var (shop, score) in new[] { (near, 3), (far, 5) })
        {
            var rating = CheckIn.CreateUnified(user, shop.Id, "author", "visit", Rating.Create(score, score, score),
                DateTime.UtcNow, CheckInVisibility.Public);
            rating.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, DateTime.UtcNow);
            db.CheckIns.Add(rating); db.DiscoveryFavorites.Add(new(user, "coffee_shop", shop.Id));
        }
        Shop(db, city, "Starbuks"); // Exact match is excluded by the personal filter.
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var request = new CoffeeShopSearchRequest { Q = "Starbuks", Filters = new()
            { City = city.PublicSlug, FavoritesOnly = true, Origin = new() { Latitude = 53.9m, Longitude = 27.56m } } };
        foreach (var sort in new[] { "rating_desc", "name_asc" })
        {
            request.Sort = sort;
            Assert.Equal(far.PublicSlug, (await store.SearchShopsAsync(request, user, Ct)).Items[0].Address.Slug);
        }
        foreach (var sort in new[] { "distance_asc", "recommended" })
        {
            request.Sort = sort;
            Assert.Equal(near.PublicSlug, (await store.SearchShopsAsync(request, user, Ct)).Items[0].Address.Slug);
        }
        request.Filters.MinRating = 4;
        Assert.Equal(far.PublicSlug, Assert.Single((await store.SearchShopsAsync(request, user, Ct)).Items).Address.Slug);
        request.Filters.RadiusKm = 0.5m;
        Assert.Empty((await store.SearchShopsAsync(request, user, Ct)).Items);
    }
}
