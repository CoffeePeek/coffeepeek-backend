using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllRoasters;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class RoasterCountsTests
{
    [Fact]
    public async Task FullList_MergesPhotosTagsAndCounts_WithoutPaging_AndReflectsChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var roaster = new Roaster("Roast"); roaster.SetPublicSlug("roast");
        var empty = new Roaster("Unused"); empty.SetPublicSlug("unused");
        db.Roasters.AddRange(roaster, empty);
        foreach (var i in Enumerable.Range(0, 101))
        {
            var other = new Roaster($"Extra {i:D3}"); other.SetPublicSlug($"extra-{i:D3}"); db.Roasters.Add(other);
        }
        roaster.ReplacePhotos([new RoasterPhoto("roast.jpg", "image/jpeg", "roasters/roast.jpg", 100, roaster.Id)]);
        var wholesale = new RoasterTag("wholesale", "Wholesale", null, 2);
        var training = new RoasterTag("training", "Training", null, 1);
        var inactive = new RoasterTag("inactive", "Hidden", null, 0);
        inactive.Update(inactive.Name, null, 0, false);
        db.RoasterTags.AddRange(wholesale, training, inactive);
        foreach (var tag in new[] { wholesale, training, inactive })
            db.RoasterTagAssignments.Add(new(roaster.Id, tag.Id, Guid.NewGuid()));
        db.RoasterTagAssignments.Add(new(empty.Id, wholesale.Id, Guid.NewGuid()));
        var shops = Enumerable.Range(0, 3).Select(i =>
            new CoffeeShop(null, $"Shop {i}", null, CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Moderate, Guid.NewGuid())).ToArray();
        foreach (var shop in shops) shop.SetRoasters([roaster]);
        shops[2].SetHidden(true);
        db.Shops.AddRange(shops);
        var products = new[] { CoffeePublicationStatus.Published, CoffeePublicationStatus.Published,
            CoffeePublicationStatus.Draft, CoffeePublicationStatus.Archived }.Select((status, i) =>
        {
            var p = new CoffeeProduct(roaster.Id, new($"Coffee {i}", null, "roasted_beans", "whole_beans", "unknown",
                null, null, null, null, null, [], [], [], []));
            p.SetPublicSlug($"coffee-{i}"); p.SetStatus(status);
            var source = new CoffeeProductSource { Product = p, CoffeeProductId = p.Id, SourceKey = "source.example",
                ExternalProductId = p.Id.ToString(), SourceUrl = "https://source.example/coffee", LastSeenAtUtc = DateTime.UtcNow };
            foreach (var weight in new[] { 250, 1000 })
                source.Variants.Add(new CoffeeVariant { Source = source, CoffeeProductSourceId = source.Id,
                    SourceKey = source.SourceKey, ExternalVariantId = $"{p.Id}-{weight}", WeightGrams = weight,
                    Price = 30, Currency = "BYN", Availability = i == 0 ? CoffeeAvailability.OutOfStock : CoffeeAvailability.InStock });
            p.Sources.Add(source);
            return p;
        }).ToArray();
        db.CoffeeProducts.AddRange(products);
        await db.SaveChangesAsync(ct);
        var media = new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" };
        var addresses = Mock.Of<IPublicAddressService>();
        var store = new CoffeeCatalogStore(db, Options.Create(media), Options.Create(new CoffeeImportOptions()),
            addresses, NullLogger<CoffeeCatalogStore>.Instance);
        var handler = new GetAllRoastersHandler();
        var response = await handler.Handle(new(), new QueryRoasterRepository(db), MapsterConfiguration.CreateMapper(media), store, ct);
        var dto = response.Data!.Roasters.Single(r => r.Id == roaster.Id);
        Assert.Equal(2, dto.CoffeeShopsCount);
        Assert.Equal(2, dto.CoffeeProductsCount);
        var unused = response.Data.Roasters.Single(r => r.Id == empty.Id);
        Assert.Equal(0, unused.CoffeeShopsCount);
        Assert.Equal(0, unused.CoffeeProductsCount);
        var all = await store.RoastersAsync([], ct);
        Assert.Equal(103, all.Length);
        var catalog = all[0]; Assert.Equal(roaster.Name, catalog.Name);
        Assert.Equal(2, catalog.CoffeeShopsCount);
        Assert.Equal(2, catalog.CoffeeProductsCount);
        Assert.Equal(1, catalog.AvailableCoffeeProducts);
        Assert.Equal("roast", catalog.Address!.Slug);
        Assert.Equal("/roasters/roast", catalog.Address.CanonicalPath);
        Assert.EndsWith("/roasters/roast.jpg", catalog.PhotoUrl);
        Assert.Equal(catalog.PhotoUrl, catalog.CoverPhoto!.FullUrl);
        Assert.Equal(new[] { "training", "wholesale" }, catalog.Tags.Select(t => t.Slug));
        Assert.Equal(all.Skip(1).Select(r => r.Name).OrderBy(n => n), all.Skip(1).Select(r => r.Name));
        Assert.Equal(2, (await store.RoastersAsync(["wholesale"], ct)).Length);
        Assert.Equal("roast", Assert.Single(await store.RoastersAsync(["wholesale", "training"], ct)).Address!.Slug);
        Assert.Empty(await store.RoastersAsync(["inactive"], ct));
        Assert.Empty(await store.RoastersAsync(["unknown"], ct));
        var emptyDto = all.Single(r => r.Name == empty.Name);
        Assert.Equal((0, 0, 0), (emptyDto.CoffeeShopsCount, emptyDto.CoffeeProductsCount, emptyDto.AvailableCoffeeProducts));

        shops[0].SetHidden(true);
        products[1].SetStatus(CoffeePublicationStatus.Archived);
        await db.SaveChangesAsync(ct);
        var updated = (await store.RoasterCountsAsync([roaster.Id, empty.Id], ct))[roaster.Id];
        Assert.Equal((1, 1), updated);
        var changed = Assert.Single(await store.RoastersAsync(["training"], ct));
        Assert.Equal((1, 1, 0), (changed.CoffeeShopsCount, changed.CoffeeProductsCount, changed.AvailableCoffeeProducts));
    }
}