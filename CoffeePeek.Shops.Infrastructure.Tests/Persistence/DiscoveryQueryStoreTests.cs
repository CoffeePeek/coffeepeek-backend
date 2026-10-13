using System.Reflection;
using System.Text.RegularExpressions;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using CoffeeProduct = CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate.CoffeeProduct;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class DiscoveryQueryStoreTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static DiscoveryQueryStore Store(ShopsDbContext db) => new(db, Mock.Of<IPublicAddressService>(),
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }));
    private static Roaster Roaster(ShopsDbContext db, string slug)
    {
        var r = new Roaster(slug); r.SetPublicSlug(slug); db.Roasters.Add(r); return r;
    }
    private static CoffeeProduct Product(ShopsDbContext db, Roaster r, string name, string? caffeine = null, string? roast = null,
        string? acidity = null, string[]? taste = null, bool published = true, string[]? brew = null)
    {
        var p = new CoffeeProduct(r.Id, new(name, null, "roasted_beans", "whole_beans", "unknown", null, null, null, null, null, [], [], [], []));
        p.SetPublicSlug(name.ToLowerInvariant().Replace(' ', '-')); p.CreatedAtUtc = DateTime.UtcNow;
        p.ApplyClassification(new(brew ?? [], caffeine, roast, acidity, [], [], taste ?? [], null));
        if (published) p.SetStatus(CoffeePublicationStatus.Published);
        db.CoffeeProducts.Add(p); return p;
    }
    private static CoffeeVariant Offer(CoffeeProduct p, int weight, decimal price, string? purpose = null, CoffeeAvailability availability = CoffeeAvailability.InStock)
    {
        var source = p.Sources.FirstOrDefault();
        if (source is null)
        {
            source = new CoffeeProductSource(p, "source.example", p.Id.ToString(), "https://source.example/item");
            source.UpdateMetadata(source.ExternalProductId, source.SourceUrl, null, null, "{}", DateTime.UtcNow);
            p.AttachSource(source);
        }
        var v = new CoffeeVariant(source, Guid.NewGuid().ToString(), weight, price, "BYN", availability);
        v.SetBrewPurpose(purpose, false); source.AttachVariant(v); return v;
    }

    [Fact]
    public async Task Offers_MatchAllCriteriaTogether_AndOnlyMatchingOffersAreReturned()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var r = Roaster(db, "roast");
        var p = Product(db, r, "One", brew: ["omni"]);
        Offer(p, 250, 50); Offer(p, 1000, 10); Offer(p, 250, 20, "filter");
        Offer(p, 250, 15, "espresso", CoffeeAvailability.OutOfStock);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var result = await store.SearchCoffeesAsync(new() { Filters = new() { WeightGrams = 250, Currency = "BYN", MaxPrice = 25, Brew = ["filter"] } }, Ct);
        var card = Assert.Single(result.Items);
        var offer = Assert.Single(card.MatchingOffers);
        Assert.Equal(20m, offer.Price); Assert.Equal("filter", offer.BrewPurpose);
        Assert.Empty((await store.SearchCoffeesAsync(new() { Filters = new() { WeightGrams = 250, Currency = "BYN", MaxPrice = 25, Brew = ["espresso"] } }, Ct)).Items);
        Assert.Equal(250, offer.WeightGrams); Assert.Equal("InStock", offer.Availability);
        Assert.DoesNotContain("ExternalVariant", offer.OfferKey);
        Assert.Equal(4, (await store.CoffeeDetailsAsync("one", Ct))!.Offers.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("name_asc")]
    [InlineData("relevance")]
    [InlineData("price_asc")]
    [InlineData("price_desc")]
    [InlineData("newest")]
    public async Task CoffeeNameSorting_PutsSpecialPrefixesLast_BeforeStablePagination(string? sort)
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var roaster = Roaster(db, "roast");
        string[] names = ["_Underscore", "*Special", "\"Quoted", "#Hash", "☕ Blend", "Alpha", "Арабика", "7 Blend", "Ёлка", "Éthiopie", "Alpha"];
        var products = names.Select((name, i) =>
        {
            var product = new CoffeeProduct(roaster.Id, new(name, null, "roasted_beans", "whole_beans", "unknown",
                null, null, null, null, null, [], [], [], []));
            product.SetPublicSlug($"coffee-{i}"); product.SetStatus(CoffeePublicationStatus.Published);
            product.CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Offer(product, 250, 30); db.CoffeeProducts.Add(product); return product;
        }).ToArray();
        Offer(Product(db, roaster, "Draft", published: false), 250, 10);
        var archived = Product(db, roaster, "Archived"); archived.SetStatus(CoffeePublicationStatus.Archived); Offer(archived, 250, 10);
        await db.SaveChangesAsync(Ct);
        var expected = products.OrderBy(p => !char.IsLetter(p.Name[0])).ThenBy(p => p.Name).ThenBy(p => p.PublicSlug);
        var slugs = new List<string>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await Store(db).SearchCoffeesAsync(new()
            {
                Q = sort == "relevance" ? "roast" : string.Empty, Sort = sort, Page = page, PageSize = 3,
                Filters = new() { Currency = "BYN", WeightGrams = 250 }
            }, Ct);
            Assert.Equal(products.Length, result.TotalItems); Assert.Equal(4, result.TotalPages);
            slugs.AddRange(result.Items.Select(p => p.Address.Slug));
        }
        Assert.Equal(expected.Select(p => p.PublicSlug), slugs);
    }

    [Theory]
    [InlineData("price_asc", 10)]
    [InlineData("price_desc", 50)]
    [InlineData("newest", 30)]
    [InlineData("relevance", 30)]
    public async Task SpecialPrefixSorting_PreservesExplicitSortPriority(string sort, decimal price)
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var roaster = Roaster(db, "roast");
        var regular = new CoffeeProduct(roaster.Id, new("Alpha _Special", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, [], [], [], []));
        regular.SetPublicSlug("regular"); regular.SetStatus(CoffeePublicationStatus.Published);
        db.CoffeeProducts.Add(regular); Offer(regular, 250, 30);
        var special = new CoffeeProduct(roaster.Id, new("_Special", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, [], [], [], []));
        special.SetPublicSlug("special"); special.SetStatus(CoffeePublicationStatus.Published);
        special.CreatedAtUtc = DateTime.UtcNow.AddDays(1); Offer(special, 250, price); db.CoffeeProducts.Add(special);
        await db.SaveChangesAsync(Ct);
        var result = await Store(db).SearchCoffeesAsync(new()
        {
            Sort = sort, Q = sort == "relevance" ? "_special" : string.Empty,
            Filters = new() { Currency = "BYN", WeightGrams = 250 }
        }, Ct);
        Assert.Equal(sort == "relevance" ? "Alpha _Special" : "_Special", result.Items[0].Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("roast")]
    public async Task DefaultCoffeeSearch_RanksFilledCardsBeforeSparseCards_AndNumberedNamesLast(string? query)
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var roaster = Roaster(db, "roast");
        var sparse = Product(db, roaster, "A Coffee"); Offer(sparse, 250, 30);
        var filled = new CoffeeProduct(roaster.Id, new("Z Coffee", "Detailed description", "roasted_beans", "whole_beans",
            "single_origin", "washed", "medium", "bright", null, null, [], [], [], []));
        filled.SetPublicSlug("z-coffee"); filled.SetStatus(CoffeePublicationStatus.Published);
        db.CoffeeProducts.Add(filled); Offer(filled, 250, 30);
        var numbered = new CoffeeProduct(roaster.Id, new("7 Coffee", "Detailed description", "roasted_beans", "whole_beans",
            "single_origin", "washed", "medium", "bright", "full", null, [], [], [], []));
        numbered.SetPublicSlug("7-coffee"); numbered.SetStatus(CoffeePublicationStatus.Published);
        db.CoffeeProducts.Add(numbered); Offer(numbered, 250, 30);
        await db.SaveChangesAsync(Ct);

        var slugs = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await Store(db).SearchCoffeesAsync(new() { Q = query ?? "", Page = page, PageSize = 1 }, Ct);
            Assert.Equal(3, result.TotalItems);
            slugs.Add(Assert.Single(result.Items).Address.Slug);
        }
        Assert.Equal(new[] { "z-coffee", sparse.PublicSlug, "7-coffee" }, slugs);
    }

    [Fact]
    public async Task PriceSorting_UsesCheapestMatchingWeight_AndPaginationRunsAfterFilters()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var r = Roaster(db, "roast"); var a = Product(db, r, "A"); var b = Product(db, r, "B");
        Offer(a, 1000, 1); Offer(a, 250, 40); Offer(a, 250, 30); Offer(b, 250, 20);
        await db.SaveChangesAsync(Ct);
        var result = await Store(db).SearchCoffeesAsync(new() { Filters = new() { Currency = "BYN", WeightGrams = 250 }, Sort = "price_asc", PageSize = 1 }, Ct);
        Assert.Equal(2, result.TotalItems); Assert.Equal(2, result.TotalPages);
        var card = Assert.Single(result.Items); Assert.Equal("B", card.Name); Assert.Equal(20m, card.SortPrice);
        var descending = await Store(db).SearchCoffeesAsync(new() { Filters = new() { Currency = "BYN", WeightGrams = 250 }, Sort = "price_desc", PageSize = 1 }, Ct);
        Assert.Equal(30m, Assert.Single(descending.Items).SortPrice);
    }

    [Fact]
    public async Task Facets_RemoveOnlyTheirOwnSelection_AndPriceRangeIgnoresPriceBounds()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var r = Roaster(db, "roast");
        Offer(Product(db, r, "Chocolate Light", roast: "light", taste: ["chocolate"]), 250, 30);
        Offer(Product(db, r, "Berry Light", roast: "light", taste: ["berry"]), 250, 60);
        Offer(Product(db, r, "Chocolate Dark", roast: "dark", taste: ["chocolate"]), 250, 70);
        await db.SaveChangesAsync(Ct);
        var facets = await Store(db).CoffeeFacetsAsync(new() { Filters = new() { Roast = ["light"], Taste = ["chocolate"], Currency = "BYN", WeightGrams = 250, MaxPrice = 40 } }, Ct);
        Assert.Equal(1, facets.TotalItems);
        var taste = facets.Groups.Single(g => g.Code == "taste");
        Assert.Equal(1, taste.Options.Single(v => v.Code == "chocolate").Count);
        Assert.Equal(0, taste.Options.Single(v => v.Code == "berry").Count);
        Assert.True(taste.Options.Single(v => v.Code == "chocolate").Selected);
        Assert.Equal(30m, facets.PriceRange!.Min); Assert.Equal(30m, facets.PriceRange.Max);
        var unpriced = await Store(db).CoffeeFacetsAsync(new() { Filters = new() { Roast = ["light"], Currency = "BYN", WeightGrams = 250, MaxPrice = 40 } }, Ct);
        Assert.Equal(60m, unpriced.PriceRange!.Max);
    }

    [Fact]
    public async Task UnknownAndInactiveClassification_AreRejected_AndHiddenPublicly()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var r = Roaster(db, "roast"); var p = Product(db, r, "Manual", caffeine: "decaf");
        var value = await db.CoffeeFilterValues.SingleAsync(v => v.GroupCode == "caffeine" && v.Code == "decaf", Ct);
        value.Update(value.Name, value.Description, value.SortOrder, false); await db.SaveChangesAsync(Ct);
        var store = Store(db);
        await Assert.ThrowsAsync<DiscoveryException>(() => store.SearchCoffeesAsync(new() { Filters = new() { Caffeine = ["decaf"] } }, Ct));
        await Assert.ThrowsAsync<DiscoveryException>(() => store.SearchCoffeesAsync(new() { Filters = new() { Roast = ["not-known"] } }, Ct));
        var manual = await store.SearchCoffeesAsync(new() { Filters = new() { AvailableOnly = false } }, Ct);
        Assert.Null(Assert.Single(manual.Items).Classification.Caffeine);
        Assert.Empty(manual.Items[0].MatchingOffers);
    }

    [Fact]
    public async Task SharedBudget_MatchesPriceBrewAndVolumeAtTheSameMenuItem()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var city = new City("Test"); city.SetPublicSlug("test-city"); db.Cities.Add(city);
        var shop = new CoffeeShop(null, "Cafe", null, CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Cheap, Guid.NewGuid());
        shop.SetPublicSlug("cafe"); shop.SetLocation(city.Id, "Street", 53.9m, 27.56m); shop.SetContact(null, null, null, null); db.Shops.Add(shop);
        var espresso = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "espresso", "Эспрессо", "Espresso", CoffeeDrinkCategory.Espresso, "", 10);
        var filter = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "batch_brew", "Фильтр", "Filter", CoffeeDrinkCategory.Filter, "", 20);
        db.CoffeeDrinkDefinitions.AddRange(espresso, filter);
        var menu = ShopMenu.Create(shop.Id); menu.MarkManuallyCaptured(DateTime.UtcNow);
        menu.ApplyManualItem(espresso.Id, MenuItemAvailability.Present, 3, 30, null);
        menu.ApplyManualItem(filter.Id, MenuItemAvailability.Present, 10, 250, null); db.ShopMenus.Add(menu);
        await db.SaveChangesAsync(Ct);
        var response = await Store(db).SearchDiscoveryAsync(new() { Filters = new() { Brew = ["filter"], Budget = new() { Currency = "BYN", MaxDrinkPrice = 5 } } }, null, Ct);
        Assert.Empty(response.CoffeeShops!.Items);
        var shops = await Store(db).SearchShopsAsync(new() { Filters = new() { Menu = new() { Brew = ["espresso"], Currency = "BYN", MaxPrice = 5 } } }, null, Ct);
        var card = Assert.Single(shops.Items); Assert.Equal("espresso", Assert.Single(card.MatchingMenuItems).BrewMethod);
        Assert.Null(card.IsOpen); Assert.Null(card.IsFavorite); Assert.Null(card.IsVisited);
    }

    [Fact]
    public void CoffeeFilters_TranslateIntoPostgresSql()
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=x").Options);
        var store = Store(db);
        var catalogType = typeof(DiscoveryQueryStore).GetNestedType("CatalogState", BindingFlags.NonPublic)!;
        var active = CoffeeFilterValue.GroupCodes.ToDictionary(g => g, _ => new HashSet<string> { "omni", "filter", "espresso", "light", "decaf", "washed", "berry", "bright", "blend", "anaerobic" });
        var catalog = Activator.CreateInstance(catalogType, active, Array.Empty<CoffeeFilterValue>())!;
        var filters = new CoffeeFilters { Brew = ["filter"], Caffeine = ["decaf"], Roast = ["light"], Acidity = ["bright"], Processing = ["washed"], Fermentation = ["anaerobic"], Taste = ["berry"], Composition = ["blend"], Currency = "BYN", WeightGrams = 250, MinPrice = 10, MaxPrice = 30 };
        var products = (IQueryable<CoffeeProduct>)typeof(DiscoveryQueryStore).GetMethod("MatchingProducts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [filters, catalog, Array.Empty<Guid>(), "кофе"])!;
        var sql = products.OrderBy(p => Regex.IsMatch(p.Name, CoffeeProductRanking.NonLetterPrefix))
            .ThenByDescending(CoffeeProductRanking.Completeness).ThenBy(p => p.Name).ThenBy(p => p.PublicSlug)
            .Skip(20).Take(20).ToQueryString();
        Assert.Contains("EXISTS", sql); Assert.Contains("CoffeeVariants", sql); Assert.Contains("LIMIT", sql);
        Assert.Contains("ORDER BY", sql); Assert.Contains("\"Name\" ~ ", sql); Assert.Contains(@"^\W|^_|^\d", sql);
        Assert.Contains("\"Description\"", sql);
        Assert.True(sql.IndexOf("ORDER BY", StringComparison.Ordinal) < sql.IndexOf("LIMIT", StringComparison.Ordinal));
    }
}
