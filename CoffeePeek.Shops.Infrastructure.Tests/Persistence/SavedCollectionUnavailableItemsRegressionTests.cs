using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class SavedCollectionUnavailableItemsRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReorderingRetainsAnExistingUnavailableShop(bool hidden)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var unavailable = Shop("unavailable");
        var other = Shop("other");
        db.Shops.AddRange(unavailable, other);
        await db.SaveChangesAsync(ct);
        var store = Store(db);
        var row = await store.CreateAsync(owner, new() { Title = "Places", Items = [Item(unavailable), Item(other)] }, ct);
        MakeUnavailable(unavailable, hidden);
        await db.SaveChangesAsync(ct);

        var updated = await store.ReplaceItemsAsync(owner, row.Id, new([Item(other), Item(unavailable)], row.Revision), ct);

        Assert.NotNull(updated);
        Assert.True(updated.Revision > row.Revision);
        Assert.Equal([other.Id, unavailable.Id], await ItemIdsAsync(db, row.Id, ct));
        Assert.Equal(2, await db.DiscoveryFavorites.CountAsync(f => f.UserId == owner, ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemovingAnotherShopRetainsAnExistingUnavailableShop(bool hidden)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var unavailable = Shop("unavailable");
        var removed = Shop("removed");
        db.Shops.AddRange(unavailable, removed);
        await db.SaveChangesAsync(ct);
        var store = Store(db);
        var row = await store.CreateAsync(owner, new() { Title = "Places", Items = [Item(unavailable), Item(removed)] }, ct);
        MakeUnavailable(unavailable, hidden);
        await db.SaveChangesAsync(ct);

        await store.ReplaceItemsAsync(owner, row.Id, new([Item(unavailable)], row.Revision), ct);

        Assert.Equal([unavailable.Id], await ItemIdsAsync(db, row.Id, ct));
        Assert.Equal(unavailable.Id, Assert.Single(await db.DiscoveryFavorites.Where(f => f.UserId == owner).ToArrayAsync(ct)).EntityId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddingANewUnavailableShopIsRejectedWithoutChangingExistingItems(bool hidden)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var unavailable = Shop("unavailable");
        var retained = Shop("retained");
        db.Shops.AddRange(unavailable, retained);
        await db.SaveChangesAsync(ct);
        var store = Store(db);
        var row = await store.CreateAsync(owner, new() { Title = "Places", Items = [Item(retained)] }, ct);
        MakeUnavailable(unavailable, hidden);
        await db.SaveChangesAsync(ct);

        var error = await Assert.ThrowsAsync<DiscoveryException>(() => store.ReplaceItemsAsync(owner, row.Id,
            new([Item(retained), Item(unavailable)], row.Revision), ct));

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("COLLECTION_CHANGED", error.ErrorCode);
        Assert.Equal([retained.Id], await ItemIdsAsync(db, row.Id, ct));
        Assert.Equal(row.Revision, (await db.SavedCollections.AsNoTracking().SingleAsync(ct)).Revision);
        Assert.Equal(retained.Id, Assert.Single(await db.DiscoveryFavorites.Where(f => f.UserId == owner).ToArrayAsync(ct)).EntityId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUnavailableShopCanStillBeRemoved(bool hidden)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var unavailable = Shop("unavailable");
        var retained = Shop("retained");
        db.Shops.AddRange(unavailable, retained);
        await db.SaveChangesAsync(ct);
        var store = Store(db);
        var row = await store.CreateAsync(owner, new() { Title = "Places", Items = [Item(unavailable), Item(retained)] }, ct);
        MakeUnavailable(unavailable, hidden);
        await db.SaveChangesAsync(ct);

        await store.ReplaceItemsAsync(owner, row.Id, new([Item(retained)], row.Revision), ct);

        Assert.Equal([retained.Id], await ItemIdsAsync(db, row.Id, ct));
        Assert.Equal(retained.Id, Assert.Single(await db.DiscoveryFavorites.Where(f => f.UserId == owner).ToArrayAsync(ct)).EntityId);
    }

    private static SavedCollectionsStore Store(ShopsDbContext db) => new(db,
        Mock.Of<IPublicAddressService>(), Mock.Of<ICollectionCoverStore>(), Mock.Of<IMessageBus>());

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CoffeeShop Shop(string slug)
    {
        var shop = new CoffeeShop(null, slug, null,
            CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Moderate, Guid.NewGuid());
        shop.SetPublicSlug(slug);
        return shop;
    }

    private static FavoriteCollectionSelection Item(CoffeeShop shop) => new() { Kind = "coffee_shop", Slug = shop.PublicSlug! };

    private static void MakeUnavailable(CoffeeShop shop, bool hidden)
    {
        if (hidden) shop.SetHidden(true);
        else shop.SetStatus(CoffeeShopStatus.TemporarilyClosed);
    }

    private static Task<Guid[]> ItemIdsAsync(ShopsDbContext db, Guid collectionId, CancellationToken ct) =>
        db.Set<SavedCollectionItem>().AsNoTracking().Where(i => i.CollectionId == collectionId)
            .OrderBy(i => i.Position).Select(i => i.EntityId).ToArrayAsync(ct);
}
