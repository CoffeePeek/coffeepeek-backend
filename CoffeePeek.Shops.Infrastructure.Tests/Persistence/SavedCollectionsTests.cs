using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Contract.Events;
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

public sealed class SavedCollectionsTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly Mock<ICollectionCoverStore> covers = new(MockBehavior.Strict);
    private readonly Mock<IMessageBus> bus = new();
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private SavedCollectionsStore Store(ShopsDbContext db) => new(db, Mock.Of<IPublicAddressService>(), covers.Object, bus.Object);
    private static ShopsDbContext Database() => new(new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static Roaster Roaster(string slug) { var row = new Roaster(slug); row.SetPublicSlug(slug); return row; }
    private static FavoriteCollectionSelection Item(string slug) => new() { Kind = "roaster", Slug = slug };
    private static UploadedPhotoDto Upload(string suffix) => new("cover.jpg", "image/jpeg", $"collection-covers/{suffix}.jpg", 100);

    [Fact]
    public async Task EmptyLibraryDefaultsPrivateAndNeverAppearsInLegacySnapshotListsOrAnonymousReads()
    {
        await using var db = Database();
        var store = Store(db);
        var created = await store.CreateAsync(owner, new() { Title = "  My places  " }, Ct);
        Assert.Equal("My places", created.Title);
        Assert.Equal("list", created.Mode);
        Assert.Equal("private", created.Visibility);
        Assert.Equal(1, created.Revision);
        Assert.Empty(created.Items);
        Assert.Null(created.CanonicalPath);
        Assert.NotNull(await store.GetAsync(owner, created.Id, Ct));
        Assert.Null(await store.GetAsync(Guid.NewGuid(), created.Id, Ct));
        var legacy = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        Assert.Null(await legacy.GetAsync(created.Id, Ct));
        Assert.Empty(await legacy.ListAsync(owner, Ct));
        Assert.Single(await store.ListAsync(owner, null, null, Ct));
        // A pre-feature snapshot binary physically cannot select private library rows.
        Assert.Empty(await db.FavoriteCollections.AsNoTracking().ToArrayAsync(Ct));
        Assert.False(await db.FavoriteCollections.AnyAsync(c => c.Id == created.Id, Ct));
    }

    [Fact]
    public async Task MetadataRemainsEditableAndStaleRevisionCannotChangeAnything()
    {
        await using var db = Database();
        var store = Store(db);
        var row = await store.CreateAsync(owner, new() { Title = "Original" }, Ct);
        var updated = await store.UpdateAsync(owner, row.Id, new() { Title = "Edited", Description = "One\nTwo",
            Visibility = "public", ExpectedRevision = row.Revision }, Ct);
        Assert.Equal("One\nTwo", updated!.Description);
        Assert.True(updated.Revision > row.Revision);
        var publicRow = await store.GetPublicAsync(row.Id, Ct);
        Assert.Equal("list", publicRow!.Mode);
        Assert.Equal("public", publicRow.Visibility);
        Assert.Equal("Edited", publicRow.Title);
        var conflict = await Assert.ThrowsAsync<DiscoveryException>(() => store.UpdateAsync(owner, row.Id,
            new() { Title = "Stale", ExpectedRevision = row.Revision, CoverUpload = Upload("never") }, Ct));
        Assert.Equal("COLLECTION_CONFLICT", conflict.ErrorCode);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("Edited", (await store.GetAsync(owner, row.Id, Ct))!.Title);
        Assert.Null(await store.UpdateAsync(Guid.NewGuid(), row.Id, new() { Title = "Foreign", ExpectedRevision = updated.Revision }, Ct));
        covers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AtomicPickerChangesOnlyOwnedMutableListsAndNeverGlobalFavoritesOrSnapshots()
    {
        await using var db = Database();
        var place = Roaster("sample"); db.Roasters.Add(place);
        db.DiscoveryFavorites.Add(new(owner, "roaster", place.Id)); await db.SaveChangesAsync(Ct);
        var legacy = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
        var snapshot = await legacy.CreateAsync(owner, new() { Title = "Snapshot", Items = [Item("sample")] }, Ct);
        var store = Store(db);
        var first = await store.CreateAsync(owner, new() { Title = "First" }, Ct);
        var second = await store.CreateAsync(owner, new() { Title = "Second" }, Ct);
        var foreign = await store.CreateAsync(Guid.NewGuid(), new() { Title = "Foreign" }, Ct);
        await Assert.ThrowsAsync<DiscoveryException>(() => store.SetMembershipAsync(owner, "roaster", "sample", [first.Id, foreign.Id], Ct));
        Assert.Empty((await store.GetAsync(owner, first.Id, Ct))!.Items);
        await Assert.ThrowsAsync<DiscoveryException>(() => store.SetMembershipAsync(owner, "roaster", "sample", [snapshot.Id], Ct));
        var selected = await store.SetMembershipAsync(owner, "roaster", "sample", [first.Id, second.Id], Ct);
        Assert.True(selected.All(s => s.ContainsItem));
        var revisions = selected.ToDictionary(s => s.Id, s => s.Revision);
        var repeated = await store.SetMembershipAsync(owner, "roaster", "sample", [first.Id, second.Id], Ct);
        Assert.All(repeated, s => Assert.Equal(revisions[s.Id], s.Revision));
        await store.SetMembershipAsync(owner, "roaster", "sample", [], Ct);
        Assert.All(await store.ListAsync(owner, "roaster", "sample", Ct), s => Assert.False(s.ContainsItem));
        Assert.Single(db.DiscoveryFavorites);
        Assert.Single((await legacy.GetAsync(snapshot.Id, Ct))!.Items);
    }

    [Fact]
    public async Task ReorderingUsesStableEntityKeysAndExistingItemsSurviveUnfavoriteAndRename()
    {
        await using var db = Database();
        var places = new[] { Roaster("first"), Roaster("second"), Roaster("third") };
        db.Roasters.AddRange(places); db.DiscoveryFavorites.AddRange(places.Select(r => new DiscoveryFavorite(owner, "roaster", r.Id)));
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        var row = await store.CreateAsync(owner, new() { Title = "Order", Items = [Item("first"), Item("second"), Item("third")] }, Ct);
        db.DiscoveryFavorites.RemoveRange(db.DiscoveryFavorites);
        places[1].SetPublicSlug("renamed"); await db.SaveChangesAsync(Ct);
        row = (await store.ReplaceItemsAsync(owner, row.Id, new([Item("third"), Item("renamed")], row.Revision), Ct))!;
        db.ChangeTracker.Clear();
        Assert.Equal(["third", "renamed"], (await store.GetAsync(owner, row.Id, Ct))!.Items.Select(i => i.Address!.Slug));
        var entity = await db.SavedCollections.Include(c => c.Items).SingleAsync(Ct);
        Assert.Equal([places[2].Id, places[1].Id], entity.Items.OrderBy(i => i.Position).Select(i => i.EntityId));
        Assert.Empty(db.DiscoveryFavorites);
    }

    [Fact]
    public async Task OnlyOneOwnedPublicLibraryCanBeFeaturedAndPrivateOrDeletedRowsClearIt()
    {
        await using var db = Database(); var store = Store(db);
        var first = await store.CreateAsync(owner, new() { Title = "First", Visibility = "public" }, Ct);
        var second = await store.CreateAsync(owner, new() { Title = "Second", Visibility = "public" }, Ct);
        var privateRow = await store.CreateAsync(owner, new() { Title = "Private" }, Ct);
        Assert.Null((await store.FeaturedAsync(owner, Ct)).CollectionId);
        await Assert.ThrowsAsync<DiscoveryException>(() => store.SetFeaturedAsync(owner, privateRow.Id, Ct));
        await Assert.ThrowsAsync<DiscoveryException>(() => store.SetFeaturedAsync(Guid.NewGuid(), first.Id, Ct));
        await store.SetFeaturedAsync(owner, first.Id, Ct);
        await store.SetFeaturedAsync(owner, second.Id, Ct);
        Assert.Equal(second.Id, (await store.PublicFeaturedAsync(owner, Ct))!.Id);
        await store.UpdateAsync(owner, second.Id, new() { Title = "Second", ExpectedRevision = second.Revision }, Ct);
        Assert.Null((await store.FeaturedAsync(owner, Ct)).CollectionId);
        Assert.Null(await store.PublicFeaturedAsync(owner, Ct));
        await store.SetFeaturedAsync(owner, first.Id, Ct);
        await store.SetFeaturedAsync(owner, null, Ct);
        Assert.Null(await store.PublicFeaturedAsync(owner, Ct));
        await store.SetFeaturedAsync(owner, first.Id, Ct);
        Assert.True(await store.DeleteAsync(owner, first.Id, Ct));
        Assert.Null((await store.FeaturedAsync(owner, Ct)).CollectionId);
    }

    [Fact]
    public async Task CoversRequireCurrentPhotoIdentityAndPublicVisibilityAndNeverExposeStorageKeys()
    {
        await using var db = Database(); var store = Store(db);
        var photoId = Guid.NewGuid(); var upload = Upload("one");
        covers.Setup(c => c.ClaimAsync(owner, It.IsAny<Guid>(), upload.StorageKey, Ct))
            .ReturnsAsync(new ClaimedCollectionCoverDto(photoId, $"collection-covers/sealed/{photoId:N}.jpg", "image/jpeg", 100));
        var row = await store.CreateAsync(owner, new() { Title = "Cover", CoverUpload = upload }, Ct);
        Assert.Contains("/saved-collections/", row.CoverPhoto!.Url);
        Assert.Null(await store.CoverAsync(null, row.Id, photoId, Ct));
        Assert.Null(await store.CoverAsync(Guid.NewGuid(), row.Id, photoId, Ct));
        Assert.Null(await store.CoverAsync(owner, row.Id, Guid.NewGuid(), Ct));
        Assert.NotNull(await store.CoverAsync(owner, row.Id, photoId, Ct));
        row = (await store.UpdateAsync(owner, row.Id, new() { Title = row.Title, Visibility = "public", ExpectedRevision = row.Revision }, Ct))!;
        await store.SetFeaturedAsync(owner, row.Id, Ct);
        Assert.NotNull(await store.CoverAsync(null, row.Id, photoId, Ct));
        var publicRow = await store.GetPublicAsync(row.Id, Ct);
        Assert.Contains("/favorite-collections/", publicRow!.CoverPhoto!.Url);
        Assert.DoesNotContain("storageKey", JsonSerializer.Serialize(publicRow, JsonSerializerOptions.Web), StringComparison.OrdinalIgnoreCase);
        row = (await store.UpdateAsync(owner, row.Id, new() { Title = row.Title, ExpectedRevision = row.Revision }, Ct))!;
        Assert.Null(await store.CoverAsync(null, row.Id, photoId, Ct));
        Assert.Null(await store.GetPublicAsync(row.Id, Ct));
        Assert.Null(await store.PublicFeaturedAsync(owner, Ct));
        row = (await store.UpdateAsync(owner, row.Id, new() { Title = row.Title, ExpectedRevision = row.Revision, RemoveCover = true }, Ct))!;
        Assert.Null(row.CoverPhoto);
        Assert.Null(await store.CoverAsync(owner, row.Id, photoId, Ct));
    }

    [Fact]
    public async Task ReplacedCoverCannotBeReusedByDelayedRequestsAndDeletingIsIdempotent()
    {
        await using var db = Database(); var store = Store(db);
        var oldId = Guid.NewGuid(); var newId = Guid.NewGuid();
        covers.Setup(c => c.ClaimAsync(owner, It.IsAny<Guid>(), Upload("old").StorageKey, Ct))
            .ReturnsAsync(new ClaimedCollectionCoverDto(oldId, "collection-covers/sealed/old.jpg", "image/jpeg", 100));
        covers.Setup(c => c.ClaimAsync(owner, It.IsAny<Guid>(), Upload("new").StorageKey, Ct))
            .ReturnsAsync(new ClaimedCollectionCoverDto(newId, "collection-covers/sealed/new.jpg", "image/jpeg", 100));
        var row = await store.CreateAsync(owner, new() { Title = "Photos", CoverUpload = Upload("old") }, Ct);
        row = (await store.UpdateAsync(owner, row.Id, new() { Title = row.Title, CoverUpload = Upload("new"), ExpectedRevision = row.Revision }, Ct))!;
        await Assert.ThrowsAsync<DiscoveryException>(() => store.UpdateAsync(owner, row.Id,
            new() { Title = row.Title, CoverUpload = Upload("old"), ExpectedRevision = row.Revision }, Ct));
        Assert.Equal(newId, (await store.GetAsync(owner, row.Id, Ct))!.CoverPhoto!.Id);
        bus.Verify(b => b.PublishAsync(It.Is<CollectionCoverReleasedEvent>(e => e.PhotoId == oldId && e.CollectionId == row.Id),
            It.IsAny<DeliveryOptions>()), Times.Once);
        Assert.True(await store.DeleteAsync(owner, row.Id, Ct));
        Assert.True(await store.DeleteAsync(owner, row.Id, Ct));
        bus.Verify(b => b.PublishAsync(It.Is<CollectionCoverReleasedEvent>(e => e.PhotoId == newId), It.IsAny<DeliveryOptions>()), Times.Once);
        Assert.Null(await store.CoverAsync(owner, row.Id, newId, Ct));
    }

    [Fact]
    public async Task MoreThanHundredMembersRemainIntactAndAccountDeletionKeepsOtherOwners()
    {
        await using var db = Database(); var store = Store(db);
        var places = Enumerable.Range(1, 125).Select(i => Roaster("place-" + i)).ToArray();
        db.Roasters.AddRange(places); db.DiscoveryFavorites.AddRange(places.Select(p => new DiscoveryFavorite(owner, "roaster", p.Id)));
        await db.SaveChangesAsync(Ct);
        var row = await store.CreateAsync(owner, new() { Title = "All", Items = places.Select(p => Item(p.PublicSlug!)).ToArray() }, Ct);
        Assert.Equal(125, row.Items.Length);
        var other = Guid.NewGuid(); var retained = await store.CreateAsync(other, new() { Title = "Retained" }, Ct);
        await store.DeleteForOwnerAsync(owner, Ct);
        await store.DeleteForOwnerAsync(owner, Ct);
        Assert.Null(await store.GetAsync(owner, row.Id, Ct));
        Assert.NotNull(await store.GetAsync(other, retained.Id, Ct));
        Assert.Equal(125, await db.DiscoveryFavorites.CountAsync(Ct));
    }
}
