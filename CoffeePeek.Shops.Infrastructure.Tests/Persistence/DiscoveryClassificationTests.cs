using System.Text.Json;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Discovery;
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

public class DiscoveryClassificationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task InitialCatalog_ContainsEightGroupsTwentyNineValuesAndFiveUnassignedServices()
    {
        await using var db = Database();
        await db.Database.EnsureCreatedAsync(Ct);
        var groups = await new DiscoveryClassificationStore(db).PublicValuesAsync(Ct);
        Assert.Equal(8, groups.Length);
        Assert.Equal(29, groups.Sum(g => g.Values.Length));
        Assert.All(groups, g => Assert.Equal("or", g.Selection));
        Assert.Equal(5, await db.RoasterTags.CountAsync(Ct));
        Assert.Empty(await db.RoasterTagAssignments.ToArrayAsync(Ct));
    }

    [Fact]
    public async Task ValueCrud_ReservesPairAfterDeactivationAndKeepsExistingAssignments()
    {
        await using var db = Database();
        await db.Database.EnsureCreatedAsync(Ct);
        var store = new DiscoveryClassificationStore(db);
        var roaster = new Roaster("Roaster"); db.Roasters.Add(roaster);
        var product = Product(roaster.Id); db.CoffeeProducts.Add(product);
        product.ApplyClassification(new([], "decaf", null, null, [], [], [], null));
        await db.SaveChangesAsync(Ct);
        var value = (await store.AdminValuesAsync(Ct)).Single(v => v.GroupCode == "caffeine" && v.Code == "decaf");
        var disabled = await store.DeactivateValueAsync(value.Id, Ct);
        Assert.False(disabled!.IsActive);
        Assert.DoesNotContain((await store.PublicValuesAsync(Ct)).Single(g => g.Code == "caffeine").Values, v => v.Code == "decaf");
        Assert.Equal("decaf", (await store.GetClassificationAsync(product.Id, Ct))!.Classification.Caffeine);
        var duplicate = await Assert.ThrowsAsync<DiscoveryException>(() => store.CreateValueAsync(new()
            { GroupCode = "caffeine", Code = "decaf", Name = "Duplicate", Description = null }, Ct));
        Assert.Equal(409, duplicate.StatusCode);
        Assert.Equal("CODE_ALREADY_EXISTS", duplicate.ErrorCode);
        var reactivated = await store.EditValueAsync(value.Id, new()
            { Name = "Без кофеина", Description = "Renamed", SortOrder = 90, IsActive = true }, Ct);
        Assert.Equal("decaf", reactivated!.Code);
        Assert.Contains((await store.PublicValuesAsync(Ct)).Single(g => g.Code == "caffeine").Values, v => v.Code == "decaf" && v.Name == "Без кофеина");
        var custom = await store.CreateValueAsync(new()
            { GroupCode = "taste", Code = "stone-fruit", Name = "Косточковые", Description = null }, Ct);
        Assert.Equal("taste", custom.GroupCode);
    }

    [Fact]
    public async Task ClassificationEdit_PreservesLegacyProtectionAndRejectsStaleOrInactiveValues()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var roaster = new Roaster("Roaster"); db.Roasters.Add(roaster);
        var product = Product(roaster.Id); product.ProtectFields(["Name", "Countries", "Photos"]);
        db.CoffeeProducts.Add(product); await db.SaveChangesAsync(Ct);
        var store = new DiscoveryClassificationStore(db);
        var original = (await store.GetClassificationAsync(product.Id, Ct))!;
        var request = new CoffeeClassificationEditRequest
        {
            Version = original.Version, ProtectedFields = ["acidity", "defaultBrewPurposes"],
            Classification = new() { DefaultBrewPurposes = ["filter"], Acidity = "bright", Caffeine = "decaf" }
        };
        var edited = (await store.EditClassificationAsync(product.Id, request, Ct))!;
        Assert.True(edited.Version > original.Version);
        Assert.Equal(["Name", "Countries", "Photos"], product.ProtectedFields);
        Assert.Equal(["acidity", "defaultBrewPurposes"], product.ClassificationProtectedFields);
        var stale = await Assert.ThrowsAsync<DiscoveryException>(() => store.EditClassificationAsync(product.Id, request, Ct));
        Assert.Equal("VERSION_CONFLICT", stale.ErrorCode);
        var decaf = (await store.AdminValuesAsync(Ct)).Single(v => v.GroupCode == "caffeine" && v.Code == "decaf");
        await store.DeactivateValueAsync(decaf.Id, Ct);
        request.Version = edited.Version;
        var inactive = await Assert.ThrowsAsync<DiscoveryException>(() => store.EditClassificationAsync(product.Id, request, Ct));
        Assert.Equal(400, inactive.StatusCode);
        Assert.Equal(edited.Version, product.Version);
    }

    [Fact]
    public async Task VariantEdit_UsesProductVersionAndAllowsProtectedNullToSelectProductDefault()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var roaster = new Roaster("Roaster"); db.Roasters.Add(roaster);
        var product = Product(roaster.Id);
        product.ApplyClassification(new(["filter"], null, null, null, [], [], [], null));
        var source = new CoffeeProductSource { Product = product, CoffeeProductId = product.Id,
            SourceKey = "roast.by", ExternalProductId = "p1", SourceUrl = "https://roast.by/product" };
        var variant = new CoffeeVariant { SourceKey = "roast.by", ExternalVariantId = "v1", Price = 30,
            Currency = "BYN", Source = source, CoffeeProductSourceId = source.Id };
        source.Variants.Add(variant); product.Sources.Add(source);
        db.CoffeeProducts.Add(product); await db.SaveChangesAsync(Ct);
        var store = new DiscoveryClassificationStore(db); var version = product.Version;
        var edited = (await store.EditVariantClassificationAsync(product.Id, variant.Id,
            new() { Version = version, BrewPurpose = "espresso", ProtectFromImport = true }, Ct))!;
        Assert.Equal("espresso", edited.Variants.Single().BrewPurpose);
        Assert.Equal(version + 1, edited.Version);
        variant.SetBrewPurpose("omni", false, fromImport: true);
        Assert.Equal("espresso", variant.BrewPurposeCode);
        var reset = (await store.EditVariantClassificationAsync(product.Id, variant.Id,
            new() { Version = edited.Version, BrewPurpose = null, ProtectFromImport = true }, Ct))!;
        variant.SetBrewPurpose("omni", false, fromImport: true);
        Assert.Null(variant.BrewPurposeCode);
        Assert.Equal(["filter"], reset.Classification.DefaultBrewPurposes);
        Assert.True(reset.Variants.Single().ProtectFromImport);
        Assert.Null(await store.EditVariantClassificationAsync(product.Id, Guid.NewGuid(),
            new() { Version = reset.Version, BrewPurpose = null }, Ct));
    }

    [Fact]
    public void Normalization_UsesExplicitFieldsAndPreservesUnknownAndProtectedAssignments()
    {
        var unknown = CoffeeClassificationNormalizer.Normalize(new CoffeeSnapshotProduct
        {
            Name = "Decaf light filter Colombia", Acidity = "4", CompositionKind = "unknown",
            Origins = [new() { CountryCode = "CO", EvidenceField = "origin" }], GrindOptions = ["Эспрессо"]
        });
        Assert.Null(unknown.Caffeine); Assert.Null(unknown.RoastLevel); Assert.Null(unknown.Acidity);
        Assert.Null(unknown.Composition); Assert.Empty(unknown.DefaultBrewPurposes);
        var misleadingEnglish = CoffeeClassificationNormalizer.Normalize(new CoffeeSnapshotProduct
            { Processing = "unwashed", TasteDescriptors = ["nutmeg"] });
        Assert.Empty(misleadingEnglish.Processing);
        Assert.Equal(["spicy"], misleadingEnglish.TasteGroups);
        var explicitValues = CoffeeClassificationNormalizer.Normalize(new CoffeeSnapshotProduct
        {
            Processing = "Натуральная полуанаэробная", Features = ["Без кофеина", "Моносорт"],
            RoastLevel = "Тёмная", Acidity = "2/5", BrewRecommendations = ["Для фильтра"],
            TasteDescriptors = ["Шоколад", "Малина"]
        });
        Assert.Equal("decaf", explicitValues.Caffeine); Assert.Equal("dark", explicitValues.RoastLevel);
        Assert.Equal("low", explicitValues.Acidity); Assert.Equal("single-origin", explicitValues.Composition);
        Assert.Equal(["natural"], explicitValues.Processing); Assert.Equal(["semi-anaerobic"], explicitValues.Fermentation);
        Assert.Equal(["chocolate", "berry"], explicitValues.TasteGroups);
        var product = Product(Guid.NewGuid());
        product.ApplyClassification(new(["espresso"], "regular", "light", "bright", [], [], [], "blend"));
        product.ProtectClassificationFields(["acidity", "defaultBrewPurposes"]);
        product.ApplyClassification(explicitValues, fromImport: true);
        Assert.Equal("decaf", product.CaffeineCode); Assert.Equal("bright", product.AcidityCode);
        Assert.Equal(["espresso"], product.DefaultBrewPurposes);
        product.ApplyClassification(unknown, fromImport: true);
        Assert.Equal("decaf", product.CaffeineCode); Assert.Equal("dark", product.RoastLevelCode);
        Assert.Equal(["natural"], product.ProcessingCodes);
    }

    [Theory]
    [InlineData("средняя обжарка кофе", "кофе для эспрессо", "medium", "espresso")]
    [InlineData("светлая обжарка", "кофе для фильтра", "light", "filter")]
    [InlineData("светлая обжарка, фильтр", "кофе для фильтра", "light", "filter")]
    [InlineData("средняя обжарка кофе, эспрессо", "кофе для эспрессо", "medium", "espresso")]
    [InlineData("тёмная обжарка кофе", "обжарка под эспрессо", "dark", "espresso")]
    [InlineData("Светлая", "под фильтр", "light", "filter")]
    [InlineData("Средняя", "под эспрессо", "medium", "espresso")]
    [InlineData("Медиум", "Эспрессо", "medium", "espresso")]
    [InlineData("тёмная, эспрессо", "Эспрессо", "dark", "espresso")]
    [InlineData("эспрессо, тёмная", "Эспрессо", "dark", "espresso")]
    public void Normalization_RecognizesExplicitSourcePhrases(string roast, string brew, string roastCode, string brewCode)
    {
        var result = CoffeeClassificationNormalizer.Normalize(new CoffeeSnapshotProduct
        {
            RoastLevel = roast, BrewRecommendations = [brew], Processing = "Двойная промывка", Acidity = "2 / 5"
        });
        Assert.Equal(roastCode, result.RoastLevel);
        Assert.Equal([brewCode], result.DefaultBrewPurposes);
        Assert.Equal(["washed"], result.Processing);
        Assert.Equal("low", result.Acidity);
    }

    [Fact]
    public async Task DailyImport_PreservesClassificationProtectionAndVariantOverride()
    {
        await using var db = Database(); await db.Database.EnsureCreatedAsync(Ct);
        var roaster = new Roaster("Test roaster"); db.Roasters.Add(roaster); await db.SaveChangesAsync(Ct);
        var catalog = new CoffeeCatalogStore(db, Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }),
            Options.Create(new CoffeeImportOptions { Roasters = new() { ["roast.by"] = new() { ["ROAST"] = roaster.Id } } }),
            Mock.Of<IPublicAddressService>(), NullLogger<CoffeeCatalogStore>.Instance);
        var first = CoffeeCatalogTests.Snapshot('a', DateTimeOffset.UtcNow.AddMinutes(-2));
        first.Snapshot.Products[0].BrewRecommendations = ["Для фильтра"];
        first.Snapshot.Products[0].Features = ["Без кофеина"];
        first.Snapshot.Products[0].Variants[0].SourceAttributes = JsonSerializer.SerializeToElement(new { roastPurpose = "espresso" });
        await catalog.ImportAsync(first, Ct);
        var product = await db.CoffeeProducts.SingleAsync(Ct);
        Assert.Equal("decaf", product.CaffeineCode);
        var store = new DiscoveryClassificationStore(db); var original = (await store.GetClassificationAsync(product.Id, Ct))!;
        var changed = (await store.EditClassificationAsync(product.Id,
            new() { Version = original.Version, ProtectedFields = ["caffeine"],
                Classification = new() { Caffeine = "lowcaf", DefaultBrewPurposes = ["filter"] } }, Ct))!;
        var variant = product.Sources.Single().Variants.Single(v => v.ExternalVariantId == "v1");
        await store.EditVariantClassificationAsync(product.Id, variant.Id,
            new() { Version = changed.Version, BrewPurpose = "filter", ProtectFromImport = true }, Ct);
        var next = CoffeeCatalogTests.Snapshot('b', first.Snapshot.CollectedAtUtc.AddMinutes(1));
        next.Snapshot.Products[0].BrewRecommendations = ["Для эспрессо"];
        next.Snapshot.Products[0].Features = ["Без кофеина"];
        next.Snapshot.Products[0].Variants[0].SourceAttributes = JsonSerializer.SerializeToElement(new { roastPurpose = "omni" });
        await catalog.ImportAsync(next, Ct);
        Assert.Equal("lowcaf", product.CaffeineCode);
        Assert.Equal(["espresso"], product.DefaultBrewPurposes);
        Assert.Equal("filter", variant.BrewPurposeCode);
    }

    private static CoffeeProduct Product(Guid roasterId) => new(roasterId,
        new("Test coffee", null, "roasted_beans", "whole_beans", "unknown", null, null, null, null, null, [], [], [], []));
}
