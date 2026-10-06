using System.Text.Json;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class CoffeeCatalogTests
{
    [Fact]
    public async Task AllCapturedCatalogues_AreAcceptedByTheImportContract()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ct = TestContext.Current.CancellationToken;
        await db.Database.EnsureCreatedAsync(ct);
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "CoffeeSnapshots"), "coffees.json", SearchOption.AllDirectories);
        Assert.Equal(15, files.Length);
        var mapping = new CoffeeImportOptions();
        var snapshots = new List<CoffeeSnapshotImportRequest>();
        foreach (var file in files)
        {
            var json = await File.ReadAllBytesAsync(file, ct);
            var snapshot = JsonSerializer.Deserialize<CoffeeSnapshotDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var source = new Uri(snapshot.Source).IdnHost;
            mapping.Roasters[source] = new();
            foreach (var brand in snapshot.Products.Select(p => p.RoasterName!).Distinct())
            { var r = new Roaster(brand); db.Roasters.Add(r); mapping.Roasters[source][brand] = r.Id; }
            snapshots.Add(new() { SourceKey = source, SnapshotId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(json)), Snapshot = snapshot });
        }
        await db.SaveChangesAsync(ct);
        var store = new CoffeeCatalogStore(db, Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(mapping), Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        foreach (var snapshot in snapshots)
            Assert.Equal("Applied", (await store.ImportAsync(snapshot, ct)).Status);
        Assert.Equal(snapshots.Sum(s => s.Snapshot.Products.Length), await db.CoffeeProducts.CountAsync(ct));
        Assert.Equal(snapshots.Sum(s => s.Snapshot.Products.Sum(p => p.Variants.Length)), await db.Set<CoffeeVariant>().CountAsync(ct));
        Assert.All(await db.CoffeeProducts.ToArrayAsync(ct), p => Assert.Equal(CoffeePublicationStatus.Draft, p.Status));
    }

    [Fact]
    public async Task DailyImport_PreservesManualEditsTagsAndPhotos_AndPublishesOnlyReviewedCoffee()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var r = new Roaster("Test roaster"); r.SetPublicSlug("test-roaster"); db.Roasters.Add(r); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = Store(db, r.Id);
        var snapshot = Snapshot('a', DateTimeOffset.UtcNow.AddMinutes(-2));
        var first = await store.ImportAsync(snapshot, TestContext.Current.CancellationToken);
        var draft = (await store.AdminListAsync(1, 20, null, TestContext.Current.CancellationToken)).Items.Single();
        Assert.Null(await store.GetAsync(draft.Slug!, TestContext.Current.CancellationToken));
        Assert.Equal("Draft", draft.Status);
        Assert.Equal("CO", draft.Countries.Single().Code);
        Assert.Contains("https://media.example/", draft.Photos.Single().FullUrl);
        Assert.Contains("https://images.example/", draft.Photos.Single().Urls!.Card);
        var tag = await store.CreateTagAsync(new("decaf", "Есть декаф", null), TestContext.Current.CancellationToken);
        await store.SetRoasterTagsAsync(r.Id, [tag.Id], Guid.NewGuid(), TestContext.Current.CancellationToken);
        var edited = await store.EditAsync(draft.Id, new(draft.Content with { Name = "Ручное название" }, ["CO"],
            ["Name", "Countries", "Photos"], "Published", draft.Version), TestContext.Current.CancellationToken);
        Assert.NotNull(await store.GetAsync(draft.Slug!, TestContext.Current.CancellationToken));
        Assert.Equal(30m, draft.Variants.First(v => v.WeightGrams == 250).Price);

        var next = Snapshot('b', snapshot.Snapshot.CollectedAtUtc.AddMinutes(1));
        next.Snapshot.Products[0].SourceProductId = "changed-parent"; // Roast grouping follows stable native SKU.
        next.Snapshot.Products[0].Name = "Название с сайта";
        next.Snapshot.Products[0].Origins[0].CountryCode = "ET";
        next.Snapshot.Products[0].Variants[0].Price = 31;
        next.Snapshot.Products[0].StoredPhotos = [];
        var second = await store.ImportAsync(next, TestContext.Current.CancellationToken);
        Assert.Equal(second.RunId, (await store.ImportAsync(next, TestContext.Current.CancellationToken)).RunId);
        Assert.Equal(1, await db.CoffeeProducts.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(first.RunId, (await store.ImportAsync(snapshot, TestContext.Current.CancellationToken)).RunId);
        var published = (await store.GetAsync(draft.Slug!, TestContext.Current.CancellationToken))!;
        Assert.Equal("Ручное название", published.Content.Name);
        Assert.Equal("CO", published.Countries.Single().Code);
        Assert.Single(published.Photos);
        Assert.Equal(31m, published.Variants.First(v => v.WeightGrams == 250).Price);
        Assert.Single(await store.RoasterTagsAsync(r.Id, TestContext.Current.CancellationToken));
        Assert.Single((await store.RoastersAsync(["decaf"], 1, 20, TestContext.Current.CancellationToken)).Items);
        // A cheap 1 kg bag cannot satisfy a 250 g price filter.
        Assert.Empty((await store.ListAsync(new() { WeightGrams = 250, Currency = "BYN", MaxPrice = 25 }, TestContext.Current.CancellationToken)).Items);
        var stale = Snapshot('c', snapshot.Snapshot.CollectedAtUtc.AddMinutes(-1));
        await Assert.ThrowsAsync<CoffeeCatalogException>(() => store.ImportAsync(stale, TestContext.Current.CancellationToken));
        Assert.Equal(31m, (await store.GetAsync(draft.Slug!, TestContext.Current.CancellationToken))!.Variants.First(v => v.WeightGrams == 250).Price);
        await Assert.ThrowsAsync<CoffeeCatalogException>(() => store.EditAsync(draft.Id,
            new(draft.Content, ["CO"], [], "Published", draft.Version), TestContext.Current.CancellationToken));
        await store.EditTagAsync(tag.Id, new(tag.Name, null, 0, false), TestContext.Current.CancellationToken);
        Assert.Empty(await store.RoasterTagsAsync(r.Id, TestContext.Current.CancellationToken));
        Assert.Single(await store.AdminRoasterTagsAsync(r.Id, TestContext.Current.CancellationToken));
        Assert.True(await store.ArchiveAsync(draft.Id, TestContext.Current.CancellationToken));
        Assert.Null(await store.GetAsync(draft.Slug!, TestContext.Current.CancellationToken));
        Assert.Equal(30, (await store.CountriesAsync(TestContext.Current.CancellationToken)).Length);
    }

    [Fact]
    public async Task Import_RejectsIncompleteDuplicateOrInvalidMoney_BeforeChangingTheCatalogue()
    {
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken); var r = new Roaster("Test"); db.Roasters.Add(r); await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = Store(db, r.Id);
        var request = Snapshot('a', DateTimeOffset.UtcNow.AddMinutes(-2));
        request.Snapshot.Complete = false;
        await Assert.ThrowsAsync<CoffeeCatalogException>(() => store.ImportAsync(request, TestContext.Current.CancellationToken));
        request.Snapshot.Complete = true; request.Snapshot.Products[0].Variants[0].Price = -1;
        await Assert.ThrowsAsync<CoffeeCatalogException>(() => store.ImportAsync(request, TestContext.Current.CancellationToken));
        request.Snapshot.Products[0].Variants[0].Price = 30;
        request.Snapshot.Products[0].Variants[1].ExternalVariantId = "v1";
        await Assert.ThrowsAsync<CoffeeCatalogException>(() => store.ImportAsync(request, TestContext.Current.CancellationToken));
        Assert.Empty(await db.CoffeeProducts.ToArrayAsync(TestContext.Current.CancellationToken));
        var price = JsonSerializer.Deserialize<CoffeeSnapshotVariant>("{\"price\":\"30.00\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(30m, price!.Price);
    }

    [Fact]
    public async Task AllUnavailableSnapshot_UpdatesStock_WhileUnexplainedEmptySnapshotIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var roaster = new Roaster("ROAST"); db.Roasters.Add(roaster); await db.SaveChangesAsync(ct);
        var store = Store(db, roaster.Id);
        await store.ImportAsync(Snapshot('a', DateTimeOffset.UtcNow.AddMinutes(-2)), ct);
        var product = await db.CoffeeProducts.SingleAsync(ct);
        product.SetStatus(CoffeePublicationStatus.Published); await db.SaveChangesAsync(ct);
        var next = Snapshot('b', DateTimeOffset.UtcNow.AddMinutes(-1));
        next.Snapshot.Products = [];
        await Assert.ThrowsAsync<CoffeeCatalogException>(() => store.ImportAsync(next, ct));
        Assert.All(await db.Set<CoffeeVariant>().ToArrayAsync(ct), v => Assert.Equal(CoffeeAvailability.InStock, v.Availability));
        next.Snapshot.Excluded = [new() { SourceProductId = "p1", Reason = "not_purchasable" }];
        var result = await store.ImportAsync(next, ct);
        Assert.Equal("Applied", result.Status); Assert.Equal(0, result.Products);
        Assert.Equal(result.RunId, (await store.ImportAsync(next, ct)).RunId);
        Assert.All(await db.Set<CoffeeVariant>().ToArrayAsync(ct), v =>
        {
            Assert.Equal(CoffeeAvailability.OutOfStock, v.Availability);
            Assert.Equal(next.Snapshot.CollectedAtUtc.UtcDateTime, v.CheckedAtUtc);
        });
        Assert.Equal(CoffeePublicationStatus.Published, product.Status);
        Assert.Empty((await store.ListAsync(new(), ct)).Items);
    }

    private static CoffeeCatalogStore Store(ShopsDbContext db, Guid roasterId) => new(db,
        Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example", ImageProxyEndpoint = "https://images.example" }),
        Options.Create(new CoffeeImportOptions { Roasters = new() { ["roast.by"] = new() { ["ROAST"] = roasterId } } }),
        Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
    internal static CoffeeSnapshotImportRequest Snapshot(char id, DateTimeOffset date) => new()
    {
        SourceKey = "roast.by", SnapshotId = new string(id, 64), Snapshot = new()
        {
            SchemaVersion = 1, Source = "https://roast.by/", Complete = true, CollectedAtUtc = date,
            Coverage = [new() { ExpectedProducts = 1, DiscoveredProducts = 1 }],
            Products = [new()
            {
                SourceKey = "roast.by", SourceProductId = "p1", SourceUrl = "https://roast.by/coffee/p1/",
                RoasterName = "ROAST", Name = "Колумбия", ProductKind = "roasted_beans", ProductForm = "whole_beans",
                Origins = [new() { CountryCode = "CO", EvidenceField = "properties.Страна" }],
                ImageUrls = ["https://roast.by/photo.jpg"],
                StoredPhotos = [new(Guid.NewGuid(), "coffees/" + new string('a', 64) + ".jpg", "coffee.jpg", "image/jpeg", 100, "https://roast.by/photo.jpg")],
                Variants = [new() { ExternalVariantId = "v1", WeightGrams = 250, Price = 30, Currency = "BYN", AvailableForPurchase = true },
                    new() { ExternalVariantId = "v2", WeightGrams = 1000, Price = 20, Currency = "BYN", AvailableForPurchase = true }]
            }]
        }
    };
}
