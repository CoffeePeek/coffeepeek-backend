using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllRoasters;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class RoasterCountsTests
{
    [Fact]
    public async Task Counts_ExcludeHiddenShopsDraftsArchivesAndPackaging_AndReflectChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var roaster = new Roaster("Roast"); roaster.SetPublicSlug("roast");
        var empty = new Roaster("Unused"); empty.SetPublicSlug("unused");
        db.Roasters.AddRange(roaster, empty);
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
        var catalog = (await store.RoastersAsync([], 1, 20, ct)).Items.Single(r => r.Roaster.Name == roaster.Name);
        Assert.Equal(2, catalog.Roaster.CoffeeShopsCount);
        Assert.Equal(2, catalog.Roaster.CoffeeProductsCount);
        Assert.Equal(1, catalog.AvailableProducts);
        var discovery = new DiscoveryQueryStore(db, addresses, Options.Create(media));
        var card = (await discovery.SearchRoastersAsync(new RoasterSearchRequest(), null, ct)).Items.Single(r => r.Name == roaster.Name);
        Assert.Equal(2, card.CoffeeShopsCount);
        Assert.Equal(2, card.CoffeeProductsCount);

        shops[0].SetHidden(true);
        products[1].SetStatus(CoffeePublicationStatus.Archived);
        await db.SaveChangesAsync(ct);
        var updated = (await store.RoasterCountsAsync([roaster.Id, empty.Id], ct))[roaster.Id];
        Assert.Equal((1, 1), updated);
    }
}