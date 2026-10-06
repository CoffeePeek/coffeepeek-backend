using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class DiscoveryFavoritesTests
{
    [Fact]
    public async Task RepeatedUpdatesAreIdempotent_AndOneUserCannotRemoveAnotherUsersFavorite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync(ct);
        var roaster = new Roaster("Favorite roaster"); roaster.SetPublicSlug("favorite-roaster");
        db.Roasters.Add(roaster); await db.SaveChangesAsync(ct);
        var store = new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>());
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await store.SetAsync(first, "roaster", "favorite-roaster", true, ct);
        await store.SetAsync(first, "roaster", "favorite-roaster", true, ct);
        await store.SetAsync(second, "roaster", "favorite-roaster", false, ct);
        Assert.Equal(1, await db.DiscoveryFavorites.CountAsync(ct));
        Assert.Empty(await store.ListAsync(second, ct));
        var favorite = Assert.Single(await store.ListAsync(first, ct));
        Assert.Equal("favorite-roaster", favorite.Address.Slug);
        Assert.Equal("/roasters/favorite-roaster", favorite.Address.CanonicalPath);
        await store.SetAsync(first, "roaster", "favorite-roaster", false, ct);
        await store.SetAsync(first, "roaster", "favorite-roaster", false, ct);
        Assert.Empty(await store.ListAsync(first, ct));
    }

    [Fact]
    public async Task ConcurrentDeletes_SucceedAndDetachTheAlreadyDeletedFavorite()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var first = new ShopsDbContext(options);
        await using var second = new ShopsDbContext(options);
        await first.Database.EnsureCreatedAsync(ct);
        var roaster = new Roaster("Concurrent"); roaster.SetPublicSlug("concurrent");
        first.Roasters.Add(roaster); await first.SaveChangesAsync(ct);
        var user = Guid.NewGuid();
        var firstStore = new DiscoveryFavoritesStore(first, Mock.Of<IPublicAddressService>());
        var secondStore = new DiscoveryFavoritesStore(second, Mock.Of<IPublicAddressService>());
        await firstStore.SetAsync(user, "roaster", "concurrent", true, ct);
        first.ChangeTracker.Clear();
        // Both requests read the same favorite before either deletes it.
        await first.DiscoveryFavorites.FindAsync([user, "roaster", roaster.Id], ct);
        await secondStore.SetAsync(user, "roaster", "concurrent", false, ct);
        await firstStore.SetAsync(user, "roaster", "concurrent", false, ct);
        Assert.Empty(await firstStore.ListAsync(user, ct));
        await firstStore.SetAsync(user, "roaster", "concurrent", true, ct);
        Assert.Single(await firstStore.ListAsync(user, ct));
    }

    [Fact]
    public async Task UnknownEntitiesAndKindsCannotBeFavorited()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var store = new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>());
        var missing = await Assert.ThrowsAsync<DiscoveryException>(() => store.SetAsync(Guid.NewGuid(), "roaster", "unknown", true, ct));
        Assert.Equal(404, missing.StatusCode);
        var invalid = await Assert.ThrowsAsync<DiscoveryException>(() => store.SetAsync(Guid.NewGuid(), "coffee", "unknown", true, ct));
        Assert.Equal(400, invalid.StatusCode);
        Assert.Empty(await db.DiscoveryFavorites.ToArrayAsync(ct));
    }
}
