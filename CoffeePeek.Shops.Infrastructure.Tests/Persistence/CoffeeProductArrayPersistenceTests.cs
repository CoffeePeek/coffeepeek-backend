using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CoffeeProductArrayPersistenceTests
{
    [Fact]
    public async Task EfUsesPrivateArrayFieldsAndPersistsDomainChanges()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase("product-arrays-" + Guid.NewGuid())
            .Options;
        var content = new CoffeeContent("Field coffee", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, ["chocolate"], ["espresso"], ["whole"], ["washed"]);
        var product = new CoffeeProduct(Guid.NewGuid(), content);
        product.ProtectFields([nameof(CoffeeProduct.Name)]);
        var ct = TestContext.Current.CancellationToken;

        await using (var seed = new ShopsDbContext(options))
        {
            var type = seed.Model.FindEntityType(typeof(CoffeeProduct))!;
            Assert.Equal("_tasteDescriptors", type.FindProperty(nameof(CoffeeProduct.TasteDescriptors))!.FieldInfo!.Name);
            Assert.Equal("_protectedFields", type.FindProperty(nameof(CoffeeProduct.ProtectedFields))!.FieldInfo!.Name);
            seed.CoffeeProducts.Add(product);
            await seed.SaveChangesAsync(ct);
        }

        await using var verify = new ShopsDbContext(options);
        var loaded = await verify.CoffeeProducts.SingleAsync(p => p.Id == product.Id, ct);
        loaded.TasteDescriptors[0] = "outside";
        loaded.ProtectedFields[0] = nameof(CoffeeProduct.Description);
        Assert.Equal("chocolate", loaded.TasteDescriptors[0]);
        Assert.True(loaded.IsProtected(nameof(CoffeeProduct.Name)));
        Assert.False(loaded.IsProtected(nameof(CoffeeProduct.Description)));
    }

    [Fact]
    public async Task EfPersistsPrivateCountryAndSourceCollections()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase("product-children-" + Guid.NewGuid())
            .Options;
        var product = new CoffeeProduct(Guid.NewGuid(), new CoffeeContent("Children coffee", null,
            "roasted_beans", "whole_beans", "unknown", null, null, null, null, null, [], [], [], []));
        product.ReplaceCountries(["CO"]);
        var source = new CoffeeProductSource
        {
            Product = product,
            CoffeeProductId = product.Id,
            SourceKey = "test-source",
            ExternalProductId = "external-1",
            SourceUrl = "https://example.test/coffee"
        };
        product.AttachSource(source);
        source.AttachVariant(new CoffeeVariant
        {
            SourceKey = "test-source", ExternalVariantId = "variant-1", Price = 10,
            Currency = "BYN", Availability = CoffeeAvailability.InStock
        });
        source.AttachPhoto(new CoffeeProductPhoto
        {
            SourceUrl = "https://example.test/photo.jpg", StorageKey = "coffee/photo.jpg",
            FileName = "photo.jpg", ContentType = "image/jpeg", SizeBytes = 100
        });
        source.SetReviewWarnings(["review"]);
        var ct = TestContext.Current.CancellationToken;

        await using (var seed = new ShopsDbContext(options))
        {
            var type = seed.Model.FindEntityType(typeof(CoffeeProduct))!;
            Assert.Equal("_countries", type.FindNavigation(nameof(CoffeeProduct.Countries))!.FieldInfo!.Name);
            Assert.Equal("_sources", type.FindNavigation(nameof(CoffeeProduct.Sources))!.FieldInfo!.Name);
            var sourceType = seed.Model.FindEntityType(typeof(CoffeeProductSource))!;
            Assert.Equal("_variants", sourceType.FindNavigation(nameof(CoffeeProductSource.Variants))!.FieldInfo!.Name);
            Assert.Equal("_photos", sourceType.FindNavigation(nameof(CoffeeProductSource.Photos))!.FieldInfo!.Name);
            Assert.Equal("_reviewWarnings", sourceType.FindProperty(nameof(CoffeeProductSource.ReviewWarnings))!.FieldInfo!.Name);
            seed.CoffeeProducts.Add(product);
            await seed.SaveChangesAsync(ct);
        }

        await using var verify = new ShopsDbContext(options);
        var loaded = await verify.CoffeeProducts.Include(p => p.Countries)
            .Include(p => p.Sources).ThenInclude(s => s.Variants)
            .Include(p => p.Sources).ThenInclude(s => s.Photos)
            .SingleAsync(p => p.Id == product.Id, ct);
        Assert.Equal("CO", Assert.Single(loaded.Countries).CountryCode);
        var loadedSource = Assert.Single(loaded.Sources);
        Assert.Equal("external-1", loadedSource.ExternalProductId);
        Assert.Equal("variant-1", Assert.Single(loadedSource.Variants).ExternalVariantId);
        Assert.Equal("coffee/photo.jpg", Assert.Single(loadedSource.Photos).StorageKey);
        loadedSource.ReviewWarnings[0] = "changed";
        Assert.Equal("review", loadedSource.ReviewWarnings[0]);
    }
}
