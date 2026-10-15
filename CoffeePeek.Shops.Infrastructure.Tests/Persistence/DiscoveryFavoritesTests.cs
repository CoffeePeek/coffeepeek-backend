using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
        Assert.Equal("favorite-roaster", favorite.Address!.Slug);
        Assert.Equal("/roasters/favorite-roaster", favorite.Address.CanonicalPath);
        await store.SetAsync(first, "roaster", "favorite-roaster", false, ct);
        await store.SetAsync(first, "roaster", "favorite-roaster", false, ct);
        Assert.Empty(await store.ListAsync(first, ct));
    }

    [Fact]
    public async Task DrinkFavorites_UseStableCatalogIdentity_AreIdempotent_AndIsolatedByUser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "matcha_latte", "Матча латте", "Matcha latte",
            CoffeeDrinkCategory.Espresso, "", 0);
        db.CoffeeDrinkDefinitions.Add(drink);
        await db.SaveChangesAsync(ct);
        var store = new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>());
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await store.SetAsync(first, "drink", drink.Slug, true, ct);
        await store.SetAsync(first, "drink", drink.Slug, true, ct);
        await store.SetAsync(second, "drink", drink.Slug, false, ct);
        var row = Assert.Single(await db.DiscoveryFavorites.ToArrayAsync(ct));
        Assert.Equal(drink.Id, row.EntityId);
        Assert.Empty(await store.ListAsync(second, ct));
        var favorite = Assert.Single(await store.ListAsync(first, ct));
        Assert.Equal("drink", favorite.Kind);
        Assert.Equal(drink.Slug, favorite.Drink!.Slug);
        Assert.Equal("Матча латте", favorite.Name);
        Assert.Null(favorite.Address);
        Assert.Null(favorite.CoverPhoto);
        await store.SetAsync(first, "drink", drink.Slug, false, ct);
        await store.SetAsync(first, "drink", drink.Slug, false, ct);
        Assert.Empty(await store.ListAsync(first, ct));
    }

    [Theory]
    [InlineData("missing", 404)]
    [InlineData("inactive-drink", 404)]
    [InlineData("Not Valid", 400)]
    public async Task MissingInactiveOrMalformedDrinks_CannotBeFavorited(string slug, int status)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.CoffeeDrinkDefinitions.Add(CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "inactive-drink", "Скрытый", "Hidden",
            CoffeeDrinkCategory.Filter, "", 0, false));
        await db.SaveChangesAsync(ct);
        var store = new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>());
        var exception = await Assert.ThrowsAsync<DiscoveryException>(() => store.SetAsync(Guid.NewGuid(), "drink", slug, true, ct));
        Assert.Equal(status, exception.StatusCode);
        Assert.Empty(await db.DiscoveryFavorites.ToArrayAsync(ct));
    }

    [Fact]
    public async Task Cards_HaveNamesOrderedCoverPhotosAndCanonicalAddresses_AndHideUnavailableTargets()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var shop = new CoffeeShop(null, "Территория кофе", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetPublicSlug("territory");
        shop.AddPhotos([new ShopPhoto("last.jpg", "image/jpeg", "shops/last.jpg", 5, Guid.NewGuid(), 1),
            new ShopPhoto("first.jpg", "image/jpeg", "shops/first.jpg", 5, Guid.NewGuid(), 0)]);
        shop.ReorderPhotos(shop.ShopPhotos.OrderBy(p => p.FileName).Select(p => p.Id).ToArray());
        var roaster = new Roaster("Memories"); roaster.SetPublicSlug("memories");
        roaster.ReplacePhotos([new RoasterPhoto("logo.jpg", "image/jpeg", "roasters/logo.jpg", 5, roaster.Id)]);
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "profile-drink", "Матча", "Matcha", CoffeeDrinkCategory.Filter, "", 0);
        db.Shops.Add(shop); db.Roasters.Add(roaster); db.CoffeeDrinkDefinitions.Add(drink);
        var user = Guid.NewGuid();
        db.DiscoveryFavorites.AddRange(new DiscoveryFavorite(user, "coffee_shop", shop.Id),
            new DiscoveryFavorite(user, "roaster", roaster.Id), new DiscoveryFavorite(user, "drink", drink.Id));
        await db.SaveChangesAsync(ct);
        var store = new DiscoveryFavoritesStore(db, Mock.Of<IPublicAddressService>(),
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example" }));
        var favorites = await store.ListAsync(user, ct);
        var shopCard = Assert.Single(favorites, f => f.Kind == "coffee_shop");
        Assert.Equal(shop.Name, shopCard.Name);
        Assert.Equal("/coffee-shops/territory", shopCard.Address!.CanonicalPath);
        Assert.EndsWith("/shops/first.jpg", shopCard.CoverPhoto!.FullUrl);
        var roasterCard = Assert.Single(favorites, f => f.Kind == "roaster");
        Assert.Equal(roaster.Name, roasterCard.Name);
        Assert.Equal("/roasters/memories", roasterCard.Address!.CanonicalPath);
        Assert.EndsWith("/roasters/logo.jpg", roasterCard.CoverPhoto!.FullUrl);
        shop.SetHidden(true);
        drink.Update(drink.NameRu, drink.NameEn, drink.Category, drink.Aliases, drink.SortOrder, false);
        await db.SaveChangesAsync(ct);
        Assert.Equal("roaster", Assert.Single(await store.ListAsync(user, ct)).Kind);
        shop.SetHidden(false); shop.SetStatus(CoffeeShopStatus.PermanentlyClosed);
        await db.SaveChangesAsync(ct);
        Assert.Equal("roaster", Assert.Single(await store.ListAsync(user, ct)).Kind);
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
