using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CoffeeChildConcurrencyLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public CoffeeChildConcurrencyLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("offer", false)]
    [InlineData("offer", true)]
    [InlineData("photo", false)]
    [InlineData("photo", true)]
    public async Task ChildAndManualWrites_ConflictInBothOrders_WithoutPartialChanges(string operation, bool manualFirst)
    {
        var ct = TestContext.Current.CancellationToken;
        var roaster = new Roaster("Child concurrency " + Guid.NewGuid());
        var product = new CoffeeProduct(roaster.Id, new("Original", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, [], [], [], []));
        var source = new CoffeeProductSource(product, "test.example", Guid.NewGuid().ToString("N"), "https://test.example/original");
        source.AttachVariant(new CoffeeVariant(source, Guid.NewGuid().ToString("N"), 250, 10, "BYN"));
        source.AttachPhoto(new CoffeeProductPhoto("coffees/" + Guid.NewGuid() + ".jpg", "https://test.example/photo.jpg",
            "original.jpg", "image/jpeg", 100, Guid.NewGuid()));
        product.AttachSource(source);
        await using (var seed = fixture.Context(false))
        { seed.Roasters.Add(roaster); seed.CoffeeProducts.Add(product); await seed.SaveChangesAsync(ct); }
        await using var manualDb = fixture.Context(false);
        await using var childDb = fixture.Context(false);
        var manual = await LoadAsync(manualDb);
        var child = await LoadAsync(childDb);
        manual.ApplyContent(new("Manual", null, "roasted_beans", "whole_beans", "unknown",
            null, null, null, null, null, [], [], [], []));
        manual.ProtectFields([nameof(CoffeeProduct.Name)]);
        var changedSource = child.Sources.Single();
        switch (operation)
        {
            case "source":
                changedSource.UpdateMetadata(changedSource.ExternalProductId, "https://test.example/changed", null,
                    "Changed seller", "{}", DateTime.UtcNow); break;
            case "offer":
                changedSource.Variants.Single().UpdateOffer(500, 20, "BYN", CoffeeAvailability.InStock, "online", DateTime.UtcNow); break;
            case "photo":
                var photo = changedSource.Photos.Single();
                changedSource.UpdatePhoto(photo, photo.SourceUrl, "changed.jpg", photo.ContentType, 200, photo.MediaPhotoId, 1); break;
        }
        var first = manualFirst ? manualDb : childDb; var second = manualFirst ? childDb : manualDb;
        await first.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));

        await using var verify = fixture.Context(false);
        var persisted = await LoadAsync(verify); var persistedSource = persisted.Sources.Single();
        Assert.Equal(manualFirst ? "Manual" : "Original", persisted.Name);
        Assert.Equal(manualFirst ? [nameof(CoffeeProduct.Name)] : Array.Empty<string>(), persisted.ProtectedFields);
        Assert.Equal(!manualFirst && operation == "source" ? "Changed seller" : null, persistedSource.SellerName);
        Assert.Equal(!manualFirst && operation == "offer" ? 20 : 10, persistedSource.Variants.Single().Price);
        Assert.Equal(!manualFirst && operation == "photo" ? "changed.jpg" : "original.jpg", persistedSource.Photos.Single().FileName);
        Assert.Equal(manualFirst ? manual.Version : child.Version, persisted.Version);

        Task<CoffeeProduct> LoadAsync(ShopsDbContext db) => db.CoffeeProducts.Include(p => p.Sources).ThenInclude(s => s.Variants)
            .Include(p => p.Sources).ThenInclude(s => s.Photos).SingleAsync(p => p.Id == product.Id, ct);
    }
}
