using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Infrastructure.Tests.Persistence;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class DiscoveryQueryLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public DiscoveryQueryLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    private static string Key() => "live-" + Guid.NewGuid().ToString("N")[..12];
    private static DiscoveryQueryStore Store(ShopsDbContext db) => new(db, Mock.Of<IPublicAddressService>(),
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }));
    private static Roaster AddRoaster(ShopsDbContext db, string slug)
    {
        var r = new Roaster(slug); r.SetPublicSlug(slug); db.Roasters.Add(r); return r;
    }
    private static CoffeeProduct AddProduct(ShopsDbContext db, Roaster r, string name, string? caffeine = null, string? roast = null,
        string[]? taste = null, string[]? brew = null)
    {
        var p = new CoffeeProduct(r.Id, new(name, null, "roasted_beans", "whole_beans", "unknown", null, null, null, null, null, [], [], [], []));
        p.SetPublicSlug(Key()); p.CreatedAtUtc = DateTime.UtcNow;
        p.ApplyClassification(new(brew ?? [], caffeine, roast, null, [], [], taste ?? [], null));
        p.SetStatus(CoffeePublicationStatus.Published); db.CoffeeProducts.Add(p); return p;
    }
    private static void AddOffer(CoffeeProduct p, int weight, decimal price, string? brew = null, CoffeeAvailability availability = CoffeeAvailability.InStock)
    {
        var source = p.Sources.FirstOrDefault();
        if (source is null)
        {
            source = new CoffeeProductSource(p, "live.example", p.Id.ToString(), "https://live.example/coffee");
            source.UpdateMetadata(source.ExternalProductId, source.SourceUrl, null, null, "{}", DateTime.UtcNow);
            p.AttachSource(source);
        }
        var v = new CoffeeVariant(source, Guid.NewGuid().ToString(), weight, price, "BYN", availability);
        v.SetBrewPurpose(brew, false); source.AttachVariant(v);
    }

    [Fact]
    public async Task NormalizedShopSearch_UsesTrigramIndexes_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        await db.Database.OpenConnectionAsync(Ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SET enable_seqscan = off";
        await command.ExecuteNonQueryAsync(Ct);
        command.CommandText = """
            EXPLAIN SELECT "Id" FROM "Shops"
            WHERE replace(lower("Name"), 'ё', 'е') ILIKE '%кофеек%'
               OR replace(lower("Address"), 'ё', 'е') ILIKE '%кофеек%'
            """;
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var lines = new List<string>();
        while (await reader.ReadAsync(Ct)) lines.Add(reader.GetString(0));
        var plan = string.Join("\n", lines);
        Assert.Contains("IX_Shops_NormalizedName_GIN", plan);
        Assert.Contains("IX_Shops_NormalizedAddress_GIN", plan);
    }

    [Theory]
    [InlineData("кофеек", false)]
    [InlineData("кофеёк", false)]
    [InlineData("  КОФЕЕК  ", false)]
    [InlineData("КОФЕЁК", false)]
    [InlineData("кофеек", true)]
    [InlineData("кофеёк", true)]
    [InlineData("  КОФЕЕК  ", true)]
    [InlineData("КОФЕЁК", true)]
    public async Task ShopSearch_EquatesYoAndEInNamesAndAddresses_ExecuteInPostgres(string query, bool byAddress)
    {
        await using var db = fixture.Context(false);
        var city = new City(Key()); city.SetPublicSlug(Key()); db.Cities.Add(city);
        CoffeeShop AddShop(string text)
        {
            var shop = new CoffeeShop(null, byAddress ? Key() : text, null, CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Cheap, Guid.NewGuid());
            shop.SetPublicSlug(Key());
            shop.SetLocation(city.Id, byAddress ? text : "Street", 53.9m, 27.56m);
            shop.SetContact(null, null, null, null); db.Shops.Add(shop); return shop;
        }
        var yo = AddShop("Кафе КОФЕЁК");
        var e = AddShop("Кафе кофеек");
        AddShop("Кофейня");
        AddShop("Кофеёк hidden").SetHidden(true);
        AddShop("Кофеек closed").SetStatus(CoffeeShopStatus.PermanentlyClosed);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();

        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" });
        var queries = new CoffeeShopQueries(db, MapsterConfiguration.CreateMapper(media.Value),
            Mock.Of<IQueryCoffeeDrinkRepository>(), Mock.Of<IQueryShopMenuRepository>(), media);
        var request = new SearchCoffeeShopsQuery(Query: query, CityId: city.Id, PageSize: 1);
        var (first, firstCount) = await queries.Search(request, Ct);
        var (second, secondCount) = await queries.Search(request with { PageNumber = 2 }, Ct);
        Assert.Equal(2, firstCount); Assert.Equal(2, secondCount);
        var ids = new[] { Assert.Single(first).Id, Assert.Single(second).Id };
        Assert.Equal(2, ids.Distinct().Count());
        Assert.Contains(yo.Id, ids); Assert.Contains(e.Id, ids);
        Assert.Equal(yo.Name, first.Concat(second).Single(s => s.Id == yo.Id).Name);

        if (!byAddress)
        {
            var discovery = await Store(db).SearchShopsAsync(new()
            {
                Q = query, Filters = new() { City = city.PublicSlug }
            }, null, Ct);
            Assert.Equal(2, discovery.TotalItems);
            Assert.Contains(discovery.Items, s => s.Address.Slug == yo.PublicSlug);
            Assert.Contains(discovery.Items, s => s.Address.Slug == e.PublicSlug);
        }
    }

    [Theory]
    [InlineData("кофеек")]
    [InlineData("кофеёк")]
    [InlineData("КОФЕЕК")]
    [InlineData("КОФЕЁК")]
    public async Task CoffeeCatalogSearch_EquatesYoAndE_ExecuteInPostgres(string query)
    {
        await using var db = fixture.Context(false);
        var roaster = AddRoaster(db, Key());
        var yo = AddProduct(db, roaster, "Бленд КОФЕЁК"); AddOffer(yo, 250, 30);
        var e = AddProduct(db, roaster, "Бленд кофеек"); AddOffer(e, 250, 30);
        AddOffer(AddProduct(db, roaster, "Кофейный бленд"), 250, 30);
        var draft = AddProduct(db, roaster, "Кофеёк draft");
        draft.SetStatus(CoffeePublicationStatus.Draft); AddOffer(draft, 250, 30);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();

        var catalog = new CoffeeCatalogStore(db,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions()), Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        var first = await catalog.ListAsync(new() { RoasterSlug = roaster.PublicSlug, Search = query, PageSize = 1 }, Ct);
        var second = await catalog.ListAsync(new() { RoasterSlug = roaster.PublicSlug, Search = query, PageSize = 1, Page = 2 }, Ct);
        Assert.Equal(2, first.TotalItems); Assert.Equal(2, second.TotalItems);
        var slugs = new[] { Assert.Single(first.Items).Address.Slug, Assert.Single(second.Items).Address.Slug };
        Assert.Equal(2, slugs.Distinct().Count());
        Assert.Contains(yo.PublicSlug, slugs); Assert.Contains(e.PublicSlug, slugs);
        Assert.Equal(yo.Name, first.Items.Concat(second.Items).Single(p => p.Address.Slug == yo.PublicSlug).Content.Name);
    }

    [Fact]
    public async Task AllUnavailableImport_RetiresExplicitOffersWithoutChangingPublication_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        var roaster = AddRoaster(db, Key()); await db.SaveChangesAsync(Ct);
        var store = new CoffeeCatalogStore(db, Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions { Roasters = new() { ["roast.by"] = new() { ["ROAST"] = roaster.Id } } }),
            Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        var request = CoffeeCatalogTests.Snapshot('a', DateTimeOffset.UtcNow.AddMinutes(-2));
        request.SourceKey = "roast.by";
        // Keep this independent from other tests importing the same fixture identities.
        request.Snapshot.Products[0].SourceProductId = Key();
        foreach (var variant in request.Snapshot.Products[0].Variants) variant.ExternalVariantId = Key();
        request.Snapshot.Products[0].ImageUrls = []; request.Snapshot.Products[0].StoredPhotos = [];
        request.SnapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        await store.ImportAsync(request, Ct);
        var source = await db.CoffeeProductSources.Include(s => s.Product).SingleAsync(s => s.Product.RoasterId == roaster.Id, Ct);
        source.Product.SetStatus(CoffeePublicationStatus.Published); await db.SaveChangesAsync(Ct);
        var versionBeforeAvailabilityOnlyImport = source.Product.Version;
        request.SnapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        request.Snapshot.CollectedAtUtc = request.Snapshot.CollectedAtUtc.AddMinutes(1);
        request.Snapshot.Products = [];
        await Assert.ThrowsAsync<CoffeeCatalogException>(() => store.ImportAsync(request, Ct));
        request.Snapshot.Excluded = [new() { SourceProductId = source.ExternalProductId, Reason = "not_purchasable" }];
        var result = await store.ImportAsync(request, Ct);
        Assert.Equal(result.RunId, (await store.ImportAsync(request, Ct)).RunId);
        db.ChangeTracker.Clear();
        var persisted = await db.CoffeeProducts.SingleAsync(p => p.RoasterId == roaster.Id, Ct);
        Assert.Equal(CoffeePublicationStatus.Published, persisted.Status);
        Assert.True(persisted.Version > versionBeforeAvailabilityOnlyImport);
        Assert.All(await db.Set<CoffeeVariant>().Where(v => v.Source.CoffeeProductId == source.CoffeeProductId).ToArrayAsync(Ct),
            v => Assert.Equal(CoffeeAvailability.OutOfStock, v.Availability));
    }

    [Fact]
    public async Task ReportedFailureResumesAtomicallyAndPinsPayloadHash_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        var sourceKey = Key() + ".example";
        var roaster = AddRoaster(db, Key());
        await db.SaveChangesAsync(Ct);
        var store = new CoffeeCatalogStore(db,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions { Roasters = new() { [sourceKey] = new() { ["ROAST"] = roaster.Id } } }),
            Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        var request = CoffeeCatalogTests.Snapshot('a', DateTimeOffset.UtcNow.AddMinutes(-2));
        request.SourceKey = sourceKey;
        request.Snapshot.Source = "https://" + sourceKey + "/";
        request.Snapshot.Products[0].SourceKey = sourceKey;
        request.Snapshot.Products[0].SourceUrl = request.Snapshot.Source;
        request.Snapshot.Products[0].SourceProductId = Key();
        foreach (var variant in request.Snapshot.Products[0].Variants)
            variant.ExternalVariantId = Key();
        request.SnapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));

        await store.RecordFailureAsync(new CoffeeImportFailureDto(request.SourceKey, request.SnapshotId,
            request.Snapshot.CollectedAtUtc, "photos", "retry"), Ct);
        var result = await store.ImportAsync(request, Ct);
        Assert.Equal("Applied", result.Status);
        Assert.Equal(result.RunId, (await store.ImportAsync(request, Ct)).RunId);
        db.ChangeTracker.Clear();
        var run = await db.CoffeeImportRuns.SingleAsync(r => r.Id == result.RunId, Ct);
        Assert.NotEqual(request.SnapshotId, run.PayloadHash);
        Assert.Single(await db.CoffeeProducts.Where(p => p.RoasterId == roaster.Id).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentFailureReportsKeepOneRun_ExecuteInPostgres()
    {
        var sourceKey = Key() + ".example";
        var snapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var report = new CoffeeImportFailureDto(sourceKey, snapshotId,
            DateTimeOffset.UtcNow.AddMinutes(-1), "collect", "collector unavailable");
        await using var firstDb = fixture.Context(false);
        await using var secondDb = fixture.Context(false);
        CoffeeCatalogStore Store(ShopsDbContext db) => new(db,
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions { Roasters = new() { [sourceKey] = new() { ["ROAST"] = Guid.NewGuid() } } }),
            Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);

        await Task.WhenAll(Store(firstDb).RecordFailureAsync(report, Ct),
            Store(secondDb).RecordFailureAsync(report, Ct));

        await using var verify = fixture.Context(false);
        Assert.Equal(1, await verify.CoffeeImportRuns.CountAsync(
            run => run.SourceKey == sourceKey && run.SnapshotId == snapshotId, Ct));
    }

    [Fact]
    public async Task Reimport_AddsNewVariantsAndPhotosToExistingCoffee_ExecuteInPostgres()
    {
        await using var db = fixture.Context(true);
        var roaster = AddRoaster(db, Key()); await db.SaveChangesAsync(Ct);
        var sourceKey = Key() + ".example";
        var store = new CoffeeCatalogStore(db, Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions { Roasters = new() { [sourceKey] = new() { ["ROAST"] = roaster.Id } } }),
            Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        var request = CoffeeCatalogTests.Snapshot('a', DateTimeOffset.UtcNow.AddMinutes(-2));
        request.SourceKey = sourceKey; request.Snapshot.Source = "https://" + sourceKey + "/";
        request.SnapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        var item = request.Snapshot.Products[0];
        item.SourceKey = sourceKey; item.SourceProductId = Key(); item.SourceUrl = request.Snapshot.Source;
        foreach (var variant in item.Variants) variant.ExternalVariantId = Key();
        await store.ImportAsync(request, Ct);
        var product = await db.CoffeeProducts.SingleAsync(p => p.RoasterId == roaster.Id, Ct);
        product.ProtectFields(["Name"]); product.SetStatus(CoffeePublicationStatus.Published);
        await db.SaveChangesAsync(Ct);
        var productId = product.Id;
        var originalPhotoId = item.StoredPhotos[0].Id;
        var originalOffer = item.Variants[0].ExternalVariantId;
        db.ChangeTracker.Clear();

        request.SnapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        request.Snapshot.CollectedAtUtc = request.Snapshot.CollectedAtUtc.AddMinutes(1);
        item.Name = "Imported replacement name";
        item.Variants[0].Price = 31;
        var newOffer = Key();
        item.Variants = [.. item.Variants, new() { ExternalVariantId = newOffer, WeightGrams = 500, Price = 50,
            Currency = "BYN", AvailableForPurchase = true }];
        var newPhotoId = Guid.NewGuid();
        item.ImageUrls = [.. item.ImageUrls, "https://roast.by/new-photo.jpg"];
        item.StoredPhotos = [.. item.StoredPhotos, new(newPhotoId, "coffees/" + new string('b', 64) + ".jpg",
            "new-coffee.jpg", "image/jpeg", 200, "https://roast.by/new-photo.jpg")];
        var result = await store.ImportAsync(request, Ct);
        Assert.Equal("Applied", result.Status); Assert.Equal(0, result.Added);
        Assert.Equal(result.RunId, (await store.ImportAsync(request, Ct)).RunId);
        db.ChangeTracker.Clear();
        product = await db.CoffeeProducts.Include(p => p.Sources).ThenInclude(s => s.Variants)
            .Include(p => p.Sources).ThenInclude(s => s.Photos).SingleAsync(p => p.Id == productId, Ct);
        Assert.Equal("Колумбия", product.Name); Assert.Equal(CoffeePublicationStatus.Published, product.Status);
        var source = Assert.Single(product.Sources);
        Assert.Equal(3, source.Variants.Count);
        Assert.Equal(31, source.Variants.Single(v => v.ExternalVariantId == originalOffer).Price);
        Assert.Equal(50, source.Variants.Single(v => v.ExternalVariantId == newOffer).Price);
        Assert.Equal(2, source.Photos.Count);
        Assert.Contains(source.Photos, p => p.MediaPhotoId == originalPhotoId);
        Assert.Contains(source.Photos, p => p.MediaPhotoId == newPhotoId);
    }

    [Fact]
    public async Task ConcurrentFavoriteDeletes_AreIdempotent_ExecuteInPostgres()
    {
        await using var first = fixture.Context(false);
        await using var second = fixture.Context(false);
        var roaster = AddRoaster(first, Key()); await first.SaveChangesAsync(Ct);
        var user = Guid.NewGuid();
        var firstStore = new DiscoveryFavoritesStore(first, Mock.Of<IPublicAddressService>());
        var secondStore = new DiscoveryFavoritesStore(second, Mock.Of<IPublicAddressService>());
        await firstStore.SetAsync(user, "roaster", roaster.PublicSlug!, true, Ct);
        first.ChangeTracker.Clear();
        await first.DiscoveryFavorites.FindAsync([user, "roaster", roaster.Id], Ct);
        await secondStore.SetAsync(user, "roaster", roaster.PublicSlug!, false, Ct);
        await firstStore.SetAsync(user, "roaster", roaster.PublicSlug!, false, Ct);
        Assert.Empty(await firstStore.ListAsync(user, Ct));
        await firstStore.SetAsync(user, "roaster", roaster.PublicSlug!, true, Ct);
        Assert.Single(await firstStore.ListAsync(user, Ct));
    }

    [Fact]
    public async Task CoffeeNumberAndSpecialPrefixes_SortAfterLetters_BeforePagination_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        var roaster = AddRoaster(db, Key());
        foreach (var name in new[] { "_Underscore", "*Special", "\"Quoted", "☕ Blend", "Alpha", "Alpha", "Арабика", "7 Blend" })
            AddOffer(AddProduct(db, roaster, name), 250, 30);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var discovery = Store(db);
        var first = await discovery.SearchCoffeesAsync(new() { Filters = new() { Roasters = [roaster.PublicSlug!] }, PageSize = 3 }, Ct);
        var second = await discovery.SearchCoffeesAsync(new() { Filters = new() { Roasters = [roaster.PublicSlug!] }, Page = 2, PageSize = 3 }, Ct);
        var third = await discovery.SearchCoffeesAsync(new() { Filters = new() { Roasters = [roaster.PublicSlug!] }, Page = 3, PageSize = 3 }, Ct);
        Assert.Equal(8, first.TotalItems);
        Assert.All(first.Items, p => Assert.True(char.IsLetter(p.Name[0])));
        Assert.All(second.Items.Concat(third.Items), p => Assert.False(char.IsLetter(p.Name[0])));
        Assert.Contains(second.Items.Concat(third.Items), p => char.IsDigit(p.Name[0]));
        var all = await discovery.SearchCoffeesAsync(new() { Filters = new() { Roasters = [roaster.PublicSlug!] } }, Ct);
        Assert.Equal(all.Items.Select(p => p.Address.Slug), first.Items.Concat(second.Items).Concat(third.Items).Select(p => p.Address.Slug));
        var catalog = new CoffeeCatalogStore(db, Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions()), Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        var catalogFirst = await catalog.ListAsync(new() { RoasterSlug = roaster.PublicSlug, PageSize = 3 }, Ct);
        var catalogSecond = await catalog.ListAsync(new() { RoasterSlug = roaster.PublicSlug, Page = 2, PageSize = 3 }, Ct);
        var catalogThird = await catalog.ListAsync(new() { RoasterSlug = roaster.PublicSlug, Page = 3, PageSize = 3 }, Ct);
        Assert.All(catalogFirst.Items, p => Assert.True(char.IsLetter(p.Content.Name[0])));
        Assert.All(catalogSecond.Items.Concat(catalogThird.Items), p => Assert.False(char.IsLetter(p.Content.Name[0])));
        Assert.Contains(catalogSecond.Items.Concat(catalogThird.Items), p => char.IsDigit(p.Content.Name[0]));
        var catalogAll = await catalog.ListAsync(new() { RoasterSlug = roaster.PublicSlug }, Ct);
        Assert.Equal(catalogAll.Items.Select(p => p.Address.Slug), catalogFirst.Items.Concat(catalogSecond.Items).Concat(catalogThird.Items).Select(p => p.Address.Slug));
    }

    [Fact]
    public async Task MatchingOffers_PriceMinSortAndPagination_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        var r = AddRoaster(db, Key());
        var a = AddProduct(db, r, "A", "decaf", "light", brew: ["omni"]);
        AddOffer(a, 1000, 1); AddOffer(a, 250, 50); AddOffer(a, 250, 30, "filter"); AddOffer(a, 250, 2, "filter", CoffeeAvailability.OutOfStock);
        var b = AddProduct(db, r, "B", "decaf", "light", brew: ["filter"]); AddOffer(b, 250, 20);
        var draft = AddProduct(db, r, "Draft", "decaf", "light", brew: ["filter"]); draft.SetStatus(CoffeePublicationStatus.Draft); AddOffer(draft, 250, 5);
        await db.SaveChangesAsync(Ct);
        a.CreatedAtUtc = DateTime.UtcNow.AddDays(-2); b.CreatedAtUtc = DateTime.UtcNow.AddDays(-1);
        var aSource = a.Sources.First(); var bSource = b.Sources.First();
        aSource.UpdateMetadata(aSource.ExternalProductId, aSource.SourceUrl, null, null, "{}", DateTime.UtcNow);
        bSource.UpdateMetadata(bSource.ExternalProductId, bSource.SourceUrl, null, null, "{}", DateTime.UtcNow.AddHours(-1));
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var filter = new CoffeeFilters { Roasters = [r.PublicSlug!], Caffeine = ["decaf"], Roast = ["light"], Brew = ["filter"], Currency = "BYN", WeightGrams = 250, MaxPrice = 40 };
        var store = Store(db);
        var first = await store.SearchCoffeesAsync(new() { Filters = filter, Sort = "price_asc", PageSize = 1 }, Ct);
        Assert.Equal(2, first.TotalItems); Assert.Equal("B", Assert.Single(first.Items).Name); Assert.Equal(20m, first.Items[0].SortPrice);
        var second = await store.SearchCoffeesAsync(new() { Filters = filter, Sort = "price_asc", Page = 2, PageSize = 1 }, Ct);
        Assert.Equal("A", Assert.Single(second.Items).Name); Assert.Equal(30m, second.Items[0].SortPrice);
        Assert.Equal(30m, Assert.Single(second.Items[0].MatchingOffers).Price);
        Assert.Equal(4, (await store.CoffeeDetailsAsync(a.PublicSlug!, Ct))!.Offers.Length);
        var newest = await store.SearchCoffeesAsync(new() { Filters = filter, Sort = "newest" }, Ct);
        Assert.Equal("B", newest.Items[0].Name);
    }

    [Fact]
    public async Task FacetSelfExclusionAndPriceRange_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        var r = AddRoaster(db, Key());
        var chocolate = AddProduct(db, r, "Chocolate light", roast: "light", taste: ["chocolate"]);
        AddOffer(chocolate, 250, 30);
        AddOffer(AddProduct(db, r, "Berry light", roast: "light", taste: ["berry"]), 250, 60);
        AddOffer(AddProduct(db, r, "Chocolate dark", roast: "dark", taste: ["chocolate"]), 250, 70);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        // Legacy arrays can contain duplicate codes: a product still counts once per option.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "CoffeeProducts" SET "TasteGroupCodes" = ARRAY['chocolate','chocolate'] WHERE "Id" = {chocolate.Id}
            """, Ct);
        var facets = await Store(db).CoffeeFacetsAsync(new() { Filters = new() { Roasters = [r.PublicSlug!], Roast = ["light"], Taste = ["chocolate"], Currency = "BYN", WeightGrams = 250 } }, Ct);
        Assert.Equal(1, facets.TotalItems);
        Assert.Equal(1, facets.Groups.Single(g => g.Code == "taste").Options.Single(v => v.Code == "berry").Count);
        Assert.Equal(1, facets.Groups.Single(g => g.Code == "taste").Options.Single(v => v.Code == "chocolate").Count);
        Assert.Equal(1, facets.Groups.Single(g => g.Code == "roast").Options.Single(v => v.Code == "dark").Count);
        var price = await Store(db).CoffeeFacetsAsync(new() { Filters = new() { Roasters = [r.PublicSlug!], Roast = ["light"], Currency = "BYN", WeightGrams = 250, MaxPrice = 40 } }, Ct);
        Assert.Equal(1, price.TotalItems); Assert.Equal(30m, price.PriceRange!.Min); Assert.Equal(60m, price.PriceRange.Max);
    }

    [Fact]
    public async Task FullRoasterList_TagsPhotosCountsAndStock_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        var other = AddRoaster(db, Key()); var popular = AddRoaster(db, Key()); var larger = AddRoaster(db, Key());
        other.Update("A catalog", null); larger.Update("Z catalog", null);
        AddProduct(db, larger, "Unavailable one"); AddProduct(db, larger, "Unavailable two");
        var tag = new RoasterTag(Key(), "List live", null, 0); db.RoasterTags.Add(tag);
        foreach (var r in new[] { other, popular, larger }) db.RoasterTagAssignments.Add(new(r.Id, tag.Id, Guid.NewGuid()));
        AddOffer(AddProduct(db, other, "Unavailable"), 250, 20, availability: CoffeeAvailability.OutOfStock);
        var p = AddProduct(db, popular, "Available"); AddOffer(p, 250, 20); AddOffer(p, 1000, 60);
        var draft = AddProduct(db, popular, "Draft"); draft.SetStatus(CoffeePublicationStatus.Draft); AddOffer(draft, 250, 10);
        var city = new City(Key()); db.Cities.Add(city);
        var shop = ShopsLiveDatabase.NewShop("List live", city.Id); shop.SetRoasters([popular]); db.Shops.Add(shop);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var store = new CoffeeCatalogStore(db, Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions()), Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        var result = await store.RoastersAsync([tag.Slug], Ct);
        Assert.Equal(3, result.Length); Assert.Equal(popular.PublicSlug, result[0].Address!.Slug);
        Assert.Equal(larger.PublicSlug, result[1].Address!.Slug); Assert.Equal(other.PublicSlug, result[2].Address!.Slug);
        Assert.Equal((1, 1, 1), (result[0].CoffeeShopsCount, result[0].CoffeeProductsCount, result[0].AvailableCoffeeProducts));
        Assert.Equal((0, 2, 0), (result[1].CoffeeShopsCount, result[1].CoffeeProductsCount, result[1].AvailableCoffeeProducts));
        Assert.Equal((0, 1, 0), (result[2].CoffeeShopsCount, result[2].CoffeeProductsCount, result[2].AvailableCoffeeProducts));
        Assert.All(result, r => Assert.Equal(tag.Slug, Assert.Single(r.Tags).Slug));
    }

    [Fact]
    public async Task MenuBrewPriceVolumeAndGeographicOpeningFilters_ExecuteInPostgres()
    {
        await using var db = fixture.Context(false);
        var city = new City(Key()); city.SetPublicSlug(Key()); db.Cities.Add(city);
        var near = ShopsLiveDatabase.NewShop("Near", city.Id); near.SetPublicSlug(Key()); near.SetLocation(city.Id, "Near", 53.9m, 27.56m);
        var far = ShopsLiveDatabase.NewShop("Far", city.Id); far.SetPublicSlug(Key()); far.SetLocation(city.Id, "Far", 53.92m, 27.56m);
        near.AddSchedule([ShopSchedule.Create(DateTime.UtcNow.DayOfWeek, false, [new(TimeSpan.Zero, TimeSpan.FromDays(1) - TimeSpan.FromTicks(10))])]);
        db.Shops.AddRange(near, far);
        var espresso = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), Key(), "Эспрессо live", "Espresso live", CoffeeDrinkCategory.Espresso, "", 10);
        var filter = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), Key(), "Фильтр live", "Filter live", CoffeeDrinkCategory.Filter, "", 20);
        db.CoffeeDrinkDefinitions.AddRange(espresso, filter);
        var menu = ShopMenu.Create(near.Id); menu.MarkManuallyCaptured(DateTime.UtcNow);
        menu.ApplyManualItem(espresso.Id, MenuItemAvailability.Present, 3, 30, null);
        menu.ApplyManualItem(filter.Id, MenuItemAvailability.Present, 10, 250, null); db.ShopMenus.Add(menu);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var store = Store(db);
        var geo = await store.SearchShopsAsync(new() { Filters = new() { City = city.PublicSlug, Origin = new() { Latitude = 53.9m, Longitude = 27.56m }, RadiusKm = 3 }, Sort = "distance_asc" }, null, Ct);
        Assert.Equal(2, geo.TotalItems); Assert.Equal("Near", geo.Items[0].Name); Assert.InRange(geo.Items[0].DistanceMeters!.Value, 0, 1);
        Assert.InRange(geo.Items[1].DistanceMeters!.Value, 2000, 2500);
        var menuFilter = new ShopFilters { City = city.PublicSlug, Menu = new() { Brew = ["filter"], Currency = "BYN", MaxPrice = 5, VolumeMl = 250 } };
        Assert.Empty((await store.SearchShopsAsync(new() { Filters = menuFilter }, null, Ct)).Items);
        menuFilter.Menu.MaxPrice = 12;
        var menuResult = await store.SearchShopsAsync(new() { Filters = menuFilter }, null, Ct);
        Assert.Equal(10m, Assert.Single(Assert.Single(menuResult.Items).MatchingMenuItems).Price);
        var open = await store.SearchShopsAsync(new() { Filters = new() { City = city.PublicSlug, IsOpen = true } }, null, Ct);
        Assert.Equal("Near", Assert.Single(open.Items).Name);
        var rating = await store.SearchShopsAsync(new() { Filters = new() { City = city.PublicSlug, MinRating = 0 }, Sort = "rating_desc" }, null, Ct);
        Assert.Equal(2, rating.TotalItems);
    }
}
