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

public sealed class CollectionMembershipProjectionRegressionTests
{
    [Theory]
    [InlineData("coffee_shop")]
    [InlineData("roaster")]
    public async Task RemovingLastMembershipClearsBothPickerAndFavoriteCards(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var target = await AddTargetAsync(db, kind, ct);
        db.DiscoveryFavorites.Add(new(owner, kind, target));
        await db.SaveChangesAsync(ct);
        var collections = Store(db);
        var list = await collections.CreateAsync(owner, new() { Title = "Private list" }, ct);
        await collections.SetMembershipAsync(owner, kind, "membership-target", [list.Id], ct);

        var result = await collections.SetMembershipAsync(owner, kind, "membership-target", [], ct);

        Assert.All(result, item => { Assert.False(item.ContainsItem); Assert.Equal(0, item.ItemCount); });
        var loaded = await collections.ListAsync(owner, kind, "membership-target", ct);
        Assert.DoesNotContain(loaded, item => item.ContainsItem);
        Assert.Empty(await new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>()).ListAsync(owner, ct));
        Assert.Empty(await db.DiscoveryFavorites.Where(f => f.UserId == owner).ToArrayAsync(ct));
    }

    [Theory]
    [InlineData("coffee_shop")]
    [InlineData("roaster")]
    public async Task AddingMembershipDoesNotRequireAPrecedingLegacyFavoriteWrite(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var target = await AddTargetAsync(db, kind, ct);
        var collections = Store(db);
        var list = await collections.CreateAsync(owner, new() { Title = "Private list" }, ct);

        var result = await collections.SetMembershipAsync(owner, kind, "membership-target", [list.Id], ct);

        var selected = Assert.Single(result);
        Assert.True(selected.ContainsItem);
        Assert.Equal(1, selected.ItemCount);
        Assert.Equal(target, Assert.Single(await db.DiscoveryFavorites.Where(f => f.UserId == owner).ToArrayAsync(ct)).EntityId);
        Assert.Single(await new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>()).ListAsync(owner, ct));
    }

    [Theory]
    [InlineData("coffee_shop")]
    [InlineData("roaster")]
    public async Task RemovingOneMembershipKeepsTheHeartWhileAnotherListStillContainsThePlace(string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var target = await AddTargetAsync(db, kind, ct);
        db.DiscoveryFavorites.Add(new(owner, kind, target));
        await db.SaveChangesAsync(ct);
        var collections = Store(db);
        var first = await collections.CreateAsync(owner, new() { Title = "First" }, ct);
        var second = await collections.CreateAsync(owner, new() { Title = "Second", Visibility = "public" }, ct);
        await collections.SetMembershipAsync(owner, kind, "membership-target", [first.Id, second.Id], ct);

        var result = await collections.SetMembershipAsync(owner, kind, "membership-target", [second.Id], ct);

        Assert.False(result.Single(item => item.Id == first.Id).ContainsItem);
        Assert.Equal(0, result.Single(item => item.Id == first.Id).ItemCount);
        Assert.True(result.Single(item => item.Id == second.Id).ContainsItem);
        Assert.Equal(1, result.Single(item => item.Id == second.Id).ItemCount);
        Assert.Single(await new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>()).ListAsync(owner, ct));
    }

    private static SavedCollectionsStore Store(ShopsDbContext db) => new(db,
        Mock.Of<IPublicAddressService>(), Mock.Of<ICollectionCoverStore>(), Mock.Of<IMessageBus>());

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Guid> AddTargetAsync(ShopsDbContext db, string kind, CancellationToken ct)
    {
        Guid id;
        if (kind == "coffee_shop")
        {
            var shop = new CoffeeShop(null, "Membership target", null, PriceRange.Moderate, Guid.NewGuid());
            shop.SetPublicSlug("membership-target"); db.Shops.Add(shop); id = shop.Id;
        }
        else
        {
            var roaster = new Roaster("Membership target");
            roaster.SetPublicSlug("membership-target"); db.Roasters.Add(roaster); id = roaster.Id;
        }
        await db.SaveChangesAsync(ct);
        return id;
    }
}
