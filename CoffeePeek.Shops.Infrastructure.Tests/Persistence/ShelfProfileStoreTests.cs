using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Shelf;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShelfProfileStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc);
    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ShelfBag Bag(Guid owner, string name, ShelfBagPhoto? photo = null, Guid? product = null) =>
        ShelfBag.Create(Guid.NewGuid(), owner, new(product, name, "Roaster", 250, ShelfBagGrind.Beans,
            ShelfBagRoastPurpose.Filter, new DateOnly(2026, 10, 20)), photo, Now);

    [Fact]
    public async Task DefaultIsPrivateAndPreferenceIsOwnerScopedAndPersisted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var catalog = new Mock<IShelfCatalogLookup>();
        var store = new ShelfProfileStore(db, catalog.Object);
        Assert.False((await store.VisibilityAsync(owner, ct)).IsVisible);
        Assert.Null(await store.PublicAsync(owner, "author", ct));
        Assert.True((await store.SetVisibilityAsync(owner, true, ct)).IsVisible);
        db.ChangeTracker.Clear();
        Assert.True((await store.VisibilityAsync(owner, ct)).IsVisible);
        Assert.False((await store.VisibilityAsync(other, ct)).IsVisible);
        Assert.Null(await store.PublicAsync(other, "other", ct));
        Assert.Empty((await store.PublicAsync(owner, "author", ct))!.Items);
        await store.SetVisibilityAsync(owner, false, ct);
        Assert.Null(await store.PublicAsync(owner, "author", ct));
        catalog.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublicProjectionContainsOnlyOrderedActiveOwnerCardsAndPublishedCoffeeSlugs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var product = Guid.NewGuid();
        var photo = new ShelfBagPhoto(Guid.NewGuid(), "shelf-bags/sealed/private.jpg", "image/jpeg");
        var first = Bag(owner, "First", photo, product); var second = Bag(owner, "Second");
        var finished = Bag(owner, "Archive"); var foreign = Bag(other, "Other");
        var lineup = new ShelfLineup(owner, []);
        foreach (var bag in new[] { finished, second, first }) lineup.AddToFront(bag);
        lineup.Finish(finished, 5, true, Now);
        new ShelfLineup(other, []).AddToFront(foreign);
        db.ShelfBags.AddRange(first, second, finished, foreign);
        await db.SaveChangesAsync(ct);
        var catalog = new Mock<IShelfCatalogLookup>();
        catalog.Setup(c => c.GetPublishedSlugsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), ct))
            .ReturnsAsync(new Dictionary<Guid, string> { [product] = "published-coffee" });
        var store = new ShelfProfileStore(db, catalog.Object);
        await store.SetVisibilityAsync(owner, true, ct);
        var result = (await store.PublicAsync(owner, "author", ct))!;
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(["First", "Second"], result.Items.Select(i => i.Name));
        Assert.Equal("published-coffee", result.Items[0].CoffeeSlug);
        Assert.Null(result.Items[1].CoffeeSlug);
        Assert.Equal($"/api/v1/profiles/author/shelf/bags/{first.Id}/photos/{photo.Id}", result.Items[0].PhotoUrl);
        Assert.Null(result.Items[1].PhotoUrl);
        Assert.Null(await store.PublicPhotoAsync(other, first.Id, photo.Id, ct));
        Assert.Null(await store.PublicPhotoAsync(owner, first.Id, Guid.NewGuid(), ct));
        Assert.NotNull(await store.PublicPhotoAsync(owner, first.Id, photo.Id, ct));
        lineup.Finish(first, null, null, Now);
        await db.SaveChangesAsync(ct);
        Assert.Null(await store.PublicPhotoAsync(owner, first.Id, photo.Id, ct));
    }

    [Fact]
    public async Task OptingOutDuringCatalogLookupPreventsAStalePublicResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database();
        var owner = Guid.NewGuid(); var product = Guid.NewGuid(); var bag = Bag(owner, "Coffee", product: product);
        new ShelfLineup(owner, []).AddToFront(bag);
        db.ShelfBags.Add(bag); await db.SaveChangesAsync(ct);
        var catalog = new Mock<IShelfCatalogLookup>();
        var store = new ShelfProfileStore(db, catalog.Object);
        await store.SetVisibilityAsync(owner, true, ct);
        catalog.Setup(c => c.GetPublishedSlugsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), ct)).Returns(async () =>
        {
            await store.SetVisibilityAsync(owner, false, ct);
            return new Dictionary<Guid, string> { [product] = "coffee" };
        });
        Assert.Null(await store.PublicAsync(owner, "author", ct));
    }

    [Fact]
    public async Task DeletingThePreferenceIsIdempotentAndNeverDeletesThePrivateShelf()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = Database(); var owner = Guid.NewGuid();
        var bag = Bag(owner, "Private bag"); new ShelfLineup(owner, []).AddToFront(bag);
        db.ShelfBags.Add(bag); await db.SaveChangesAsync(ct);
        var store = new ShelfProfileStore(db, Mock.Of<IShelfCatalogLookup>());
        await store.SetVisibilityAsync(owner, true, ct);
        await store.DeleteForOwnerAsync(owner, ct); await store.DeleteForOwnerAsync(owner, ct);
        Assert.False((await store.VisibilityAsync(owner, ct)).IsVisible);
        Assert.Null(await store.PublicAsync(owner, "author", ct));
        Assert.Single(await db.ShelfBags.ToArrayAsync(ct));
    }
}
