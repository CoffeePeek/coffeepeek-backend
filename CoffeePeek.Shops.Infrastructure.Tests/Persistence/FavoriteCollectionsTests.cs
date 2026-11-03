using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class FavoriteCollectionsTests
{
    [Fact]
    public async Task ShopCardsUseOnlyCurrentPublicRatingsAndCounts_AndRoastersFollowAllShops()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Shop", null, CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Moderate, Guid.NewGuid());
        shop.SetPublicSlug("shop");
        shop.SetLocation(Guid.NewGuid(), "Test street, 83", 53m, 27m);
        var roaster = Roaster("Roaster", "roaster");
        db.Shops.Add(shop);
        db.Roasters.Add(roaster);
        db.DiscoveryFavorites.AddRange(new(owner, "coffee_shop", shop.Id), new(owner, "roaster", roaster.Id));
        var time = DateTime.UtcNow.AddDays(-2);
        var user = Guid.NewGuid();
        var old = ApprovedVisit(user, shop.Id, 2, time);
        var latest = ApprovedVisit(user, shop.Id, 5, time.AddHours(1));
        var other = ApprovedVisit(Guid.NewGuid(), shop.Id, 3, time);
        var privateVisit = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "private", "private", Rating.Create(1, 1, 1), time);
        var pending = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "pending", "pending", Rating.Create(1, 1, 1), time, CheckInVisibility.Public);
        db.CheckIns.AddRange(old, latest, other, privateVisit, pending);
        await db.SaveChangesAsync(ct);
        var store = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        var created = await store.CreateAsync(owner, new() { Title = "Places", Items = [
            new() { Kind = "roaster", Slug = "roaster" }, new() { Kind = "coffee_shop", Slug = "shop" }] }, ct);
        Assert.Equal(["coffee_shop", "roaster"], created.Items.Select(i => i.Kind));
        Assert.Equal(4m, created.Items[0].Rating);
        Assert.Equal(3, created.Items[0].CheckInCount);
        Assert.Equal("Test street, 83", created.Items[0].StreetAddress);
        Assert.Null(created.Items[1].Rating);
        Assert.Null(created.Items[1].CheckInCount);
        latest.ChangeVisibility(CheckInVisibility.Private);
        await db.SaveChangesAsync(ct);
        var opened = await store.GetAsync(created.Id, ct);
        Assert.Equal(2.5m, opened!.Items[0].Rating);
        Assert.Equal(2, opened.Items[0].CheckInCount);
    }

    private static CheckIn ApprovedVisit(Guid user, Guid shop, int score, DateTime time)
    {
        var visit = CheckIn.CreateUnified(user, shop, "public", "public", Rating.Create(score, score, score), time, CheckInVisibility.Public);
        visit.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, time);
        return visit;
    }

    [Fact]
    public async Task SharesOnlySelectedPlaces_InRequestedOrder_AndSurvivesUnfavoriteAndRename()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var first = Roaster("First", "first"); var second = Roaster("Second", "second"); var secret = Roaster("Unselected", "unselected");
        db.Roasters.AddRange(first, second, secret);
        db.DiscoveryFavorites.AddRange(new(owner, "roaster", first.Id), new(owner, "roaster", second.Id), new(owner, "roaster", secret.Id));
        await db.SaveChangesAsync(ct);
        var store = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        var created = await store.CreateAsync(owner, Request("second", "first"), ct);
        Assert.Equal(["Second", "First"], created.Items.Select(i => i.Name));
        Assert.Equal($"/collections/{created.Id:N}", created.CanonicalPath);
        db.DiscoveryFavorites.RemoveRange(db.DiscoveryFavorites);
        second.SetPublicSlug("new-second");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var opened = await store.GetAsync(created.Id, ct);
        Assert.NotNull(opened);
        Assert.Equal(["new-second", "first"], opened.Items.Select(i => i.Address!.Slug));
        Assert.DoesNotContain(opened.Items, i => i.Name == "Unselected");
    }

    [Fact]
    public async Task CannotShareAnotherUsersFavorites_AndRejectedRequestsDoNotCreateRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var place = Roaster("Other account", "other");
        db.Roasters.Add(place); db.DiscoveryFavorites.Add(new(Guid.NewGuid(), "roaster", place.Id));
        await db.SaveChangesAsync(ct);
        var store = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        await Assert.ThrowsAsync<DiscoveryException>(() => store.CreateAsync(Guid.NewGuid(), Request("other"), ct));
        Assert.Empty(db.FavoriteCollections);
    }

    [Fact]
    public async Task OnlyOwnerCanListAndRevoke_RevokedLinksStayUnavailable_AndDeletedTargetsDisappear()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid(); var stranger = Guid.NewGuid(); var place = Roaster("Selected", "selected");
        db.Roasters.Add(place); db.DiscoveryFavorites.Add(new(owner, "roaster", place.Id));
        await db.SaveChangesAsync(ct);
        var store = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        var created = await store.CreateAsync(owner, Request("selected"), ct);
        Assert.Empty(await store.ListAsync(stranger, ct));
        Assert.Single(await store.ListAsync(owner, ct));
        Assert.False(await store.RevokeAsync(stranger, created.Id, ct));
        Assert.NotNull(await store.GetAsync(created.Id, ct));
        db.Roasters.Remove(place); await db.SaveChangesAsync(ct);
        Assert.Empty((await store.GetAsync(created.Id, ct))!.Items);
        Assert.True(await store.RevokeAsync(owner, created.Id, ct));
        Assert.True(await store.RevokeAsync(owner, created.Id, ct));
        Assert.Null(await store.GetAsync(created.Id, ct));
        Assert.Empty(await store.ListAsync(owner, ct));
    }

    [Theory]
    [InlineData("", 1)] [InlineData(" \t", 1)] [InlineData("Valid", 0)] [InlineData("Valid", 101)]
    [InlineData("Title\nwith controls", 1)]
    public async Task RejectsInvalidTitlesAndSelectionBounds(string title, int count)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var request = new CreateFavoriteCollectionRequest { Title = title,
            Items = Enumerable.Range(0, count).Select(i => new FavoriteCollectionSelection { Kind = "roaster", Slug = "valid" }).ToArray() };
        await Assert.ThrowsAsync<DiscoveryException>(() => new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>())
            .CreateAsync(Guid.NewGuid(), request, ct));
        Assert.Empty(db.FavoriteCollections);
    }

    [Fact]
    public async Task DuplicateKindsAndSlugsAreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid(); var place = Roaster("Selected", "selected");
        db.Roasters.Add(place); db.DiscoveryFavorites.Add(new(owner, "roaster", place.Id)); await db.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<DiscoveryException>(() => new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>())
            .CreateAsync(owner, Request("selected", "selected"), ct));
        Assert.Empty(db.FavoriteCollections);
    }

    [Fact]
    public async Task PublicCardsNeverExposeHiddenOrPermanentlyClosedShops()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var shop = new CoffeeShop(null, "Shared shop", null, CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange.Moderate, Guid.NewGuid());
        shop.SetPublicSlug("shared-shop");
        db.Shops.Add(shop); db.DiscoveryFavorites.Add(new(owner, "coffee_shop", shop.Id)); await db.SaveChangesAsync(ct);
        var store = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        var request = new CreateFavoriteCollectionRequest { Title = "Selected", Items = [new() { Kind = "coffee_shop", Slug = "shared-shop" }] };
        var created = await store.CreateAsync(owner, request, ct);
        Assert.Single(created.Items);
        shop.SetHidden(true); await db.SaveChangesAsync(ct);
        Assert.Empty((await store.GetAsync(created.Id, ct))!.Items);
        shop.SetHidden(false); shop.SetStatus(CoffeeShopStatus.PermanentlyClosed); await db.SaveChangesAsync(ct);
        Assert.Empty((await store.GetAsync(created.Id, ct))!.Items);
    }

    [Fact]
    public async Task AccountDeletionRemovesOnlyItsCollectionsAndCanBeRepeated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var place = Roaster("Selected", "selected");
        db.Roasters.Add(place);
        db.DiscoveryFavorites.AddRange(new(owner, "roaster", place.Id), new(other, "roaster", place.Id));
        await db.SaveChangesAsync(ct);
        var store = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        var deleted = await store.CreateAsync(owner, Request("selected"), ct);
        var retained = await store.CreateAsync(other, Request("selected"), ct);
        await store.DeleteForOwnerAsync(owner, ct);
        await store.DeleteForOwnerAsync(owner, ct);
        db.ChangeTracker.Clear();
        Assert.Null(await store.GetAsync(deleted.Id, ct));
        Assert.NotNull(await store.GetAsync(retained.Id, ct));
        Assert.Single(await db.FavoriteCollections.Include(c => c.Items).ToArrayAsync(ct));
    }

    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Roaster Roaster(string name, string slug) { var place = new Roaster(name); place.SetPublicSlug(slug); return place; }
    private static CreateFavoriteCollectionRequest Request(params string[] slugs) => new() {
        Title = "My coffee places", Items = slugs.Select(slug => new FavoriteCollectionSelection { Kind = "roaster", Slug = slug }).ToArray() };
}
