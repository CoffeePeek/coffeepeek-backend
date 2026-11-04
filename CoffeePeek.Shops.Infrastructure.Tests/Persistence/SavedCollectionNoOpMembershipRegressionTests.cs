using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class SavedCollectionNoOpMembershipRegressionTests
{
    [Theory]
    [InlineData("coffee_shop", false)]
    [InlineData("coffee_shop", true)]
    [InlineData("roaster", false)]
    [InlineData("roaster", true)]
    public async Task EmptyMembershipSavePreservesPrivateLegacyHeartWithPublicDefault(string kind, bool delta)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var (owner, target, canonical) = await PrivateLegacyHeartAsync(db, kind, ct);
        var revision = canonical.Revision;

        var result = await Store(db).SetMembershipAsync(owner, kind, "private-heart", [], ct,
            delta ? [] : null, delta ? [] : null);

        var summary = Assert.Single(result);
        Assert.False(summary.ContainsItem);
        Assert.Equal(0, summary.ItemCount);
        Assert.Equal(revision, summary.Revision);
        await AssertPrivateLegacyHeartAsync(db, owner, kind, target, ct);
    }

    [Theory]
    [InlineData("coffee_shop")]
    [InlineData("roaster")]
    public async Task RemovingAnAlreadyAbsentMembershipPreservesPrivateLegacyHeart(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var (owner, target, canonical) = await PrivateLegacyHeartAsync(db, kind, ct);

        var result = await Store(db).SetMembershipAsync(owner, kind, "private-heart", [], ct, [], [canonical.Id]);

        Assert.False(Assert.Single(result).ContainsItem);
        Assert.Equal(canonical.Revision, Assert.Single(result).Revision);
        await AssertPrivateLegacyHeartAsync(db, owner, kind, target, ct);
    }

    [Theory]
    [InlineData("coffee_shop")]
    [InlineData("roaster")]
    public async Task EmptyDeltaIgnoresStaleFallbackSelectionAndPreservesPrivateLegacyHeart(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var (owner, target, canonical) = await PrivateLegacyHeartAsync(db, kind, ct);

        var result = await Store(db).SetMembershipAsync(owner, kind, "private-heart", [canonical.Id], ct, [], []);

        Assert.False(Assert.Single(result).ContainsItem);
        Assert.Equal(canonical.Revision, Assert.Single(result).Revision);
        await AssertPrivateLegacyHeartAsync(db, owner, kind, target, ct);
    }

    [Theory]
    [InlineData("coffee_shop", false)]
    [InlineData("coffee_shop", true)]
    [InlineData("roaster", false)]
    [InlineData("roaster", true)]
    public async Task ActualLastMembershipRemovalStillClearsTheLegacyHeart(string kind, bool delta)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var (owner, _, canonical) = await PrivateLegacyHeartAsync(db, kind, ct);
        var store = Store(db);
        await store.SetMembershipAsync(owner, kind, "private-heart", [canonical.Id], ct);

        var result = await store.SetMembershipAsync(owner, kind, "private-heart", [], ct,
            delta ? [] : null, delta ? [canonical.Id] : null);

        Assert.False(Assert.Single(result).ContainsItem);
        Assert.Equal(0, Assert.Single(result).ItemCount);
        Assert.Empty(await db.DiscoveryFavorites.Where(f => f.UserId == owner).ToArrayAsync(ct));
        Assert.Empty(await Favorites(db).ListAsync(owner, ct));
        Assert.Empty(await db.Set<SavedCollectionItem>().Where(i => i.CollectionId == canonical.Id).ToArrayAsync(ct));
    }

    private static async Task<(Guid Owner, Guid Target, SavedCollection Default)> PrivateLegacyHeartAsync(
        ShopsDbContext db, string kind, CancellationToken ct)
    {
        var owner = Guid.NewGuid();
        Guid target;
        if (kind == "coffee_shop")
        {
            var shop = new CoffeeShop(null, "Private heart", null, PriceRange.Moderate, Guid.NewGuid());
            shop.SetPublicSlug("private-heart");
            db.Shops.Add(shop);
            target = shop.Id;
        }
        else
        {
            var roaster = new Roaster("Private heart");
            roaster.SetPublicSlug("private-heart");
            db.Roasters.Add(roaster);
            target = roaster.Id;
        }
        var canonical = SavedCollection.CreateList(owner, Guid.NewGuid(), "Избранное", null, "public");
        canonical.MarkDefault();
        db.SavedCollections.Add(canonical);
        await db.SaveChangesAsync(ct);
        await Favorites(db).SetAsync(owner, kind, "private-heart", true, ct);
        return (owner, target, canonical);
    }

    private static async Task AssertPrivateLegacyHeartAsync(ShopsDbContext db, Guid owner, string kind, Guid target, CancellationToken ct)
    {
        var row = Assert.Single(await db.DiscoveryFavorites.AsNoTracking().Where(f => f.UserId == owner).ToArrayAsync(ct));
        Assert.Equal(kind, row.Kind);
        Assert.Equal(target, row.EntityId);
        Assert.Single(await Favorites(db).ListAsync(owner, ct));
        Assert.Empty(await db.Set<SavedCollectionItem>().ToArrayAsync(ct));
    }

    private static SavedCollectionsStore Store(ShopsDbContext db) => new(db,
        Mock.Of<IPublicAddressService>(), Mock.Of<ICollectionCoverStore>(), Mock.Of<IMessageBus>());

    private static DiscoveryFavoritesStore Favorites(ShopsDbContext db) => new(db, Mock.Of<IPublicAddressService>());

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
