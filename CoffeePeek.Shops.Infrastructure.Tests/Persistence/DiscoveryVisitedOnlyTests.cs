using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class DiscoveryVisitedOnlyTests
{
    [Fact]
    public async Task VisitedOnly_ExcludesDeletedCheckInsAndAgreesWithCardFlag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var userId = Guid.NewGuid();
        var deletedShop = Shop("deleted-visit");
        var activeShop = Shop("active-visit");
        var deleted = CheckIn.CreateUnified(userId, deletedShop.Id, "user", "note",
            Rating.Create(5, 5, 5), DateTime.UtcNow, CheckInVisibility.Private);
        deleted.Delete();
        var active = CheckIn.CreateUnified(userId, activeShop.Id, "user", "note",
            Rating.Create(5, 5, 5), DateTime.UtcNow, CheckInVisibility.Private);
        db.Shops.AddRange(deletedShop, activeShop);
        db.CheckIns.AddRange(deleted, active);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var store = new DiscoveryQueryStore(db, Mock.Of<IPublicAddressService>(),
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }));
        var result = await store.SearchShopsAsync(
            new CoffeeShopSearchRequest { Filters = new() { VisitedOnly = true } }, userId, ct);

        var card = Assert.Single(result.Items);
        Assert.Equal("active-visit", card.Address.Slug);
        Assert.True(card.IsVisited);
        Assert.Equal(1, result.TotalItems);
    }

    private static CoffeeShop Shop(string slug)
    {
        var shop = new CoffeeShop(null, slug, null,
            CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Cheap, Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Street", 53m, 27m);
        shop.SetContact(null, null, null, null);
        shop.SetPublicSlug(slug);
        return shop;
    }
}
