using System.Text.Json.Nodes;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Npgsql;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class SavedCollectionsMigrationLiveTests
{
    private const string Previous = "20261102130458_AddFavoriteCollections";
    private const string Migration = "20261103135540_AddSavedCollectionLibrary";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShopsDbContext Context(string connection) => new(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
    private static FavoriteCollectionSelection Item(string slug) => new() { Kind = "roaster", Slug = slug };
    private static SavedCollectionsStore Store(ShopsDbContext db, ICollectionCoverStore? covers = null) =>
        new(db, Mock.Of<IPublicAddressService>(), covers ?? Mock.Of<ICollectionCoverStore>(), Mock.Of<IMessageBus>());

    [Fact]
    public async Task PopulatedUpgradeAndSchemaRollbackPreserveSnapshotRowsKeysAndOrderAndNeverExposePrivateRowsToLegacyQuery()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var connection = await LivePostgresServer.CreateDatabaseAsync("savedlibraryupgrade", Ct);
        try
        {
            await using var db = Context(connection);
            await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);
            var owner = Guid.NewGuid();
            var favorites = await SeedPlacesAsync(db, owner);
            var snapshots = new FavoriteCollectionsStore(db, Mock.Of<IPublicAddressService>());
            // Seed the historical schema directly: the current writer also reads the later saved-library tables.
            var snapshot = new FavoriteCollection(owner, "Historical snapshot", favorites);
            db.FavoriteCollections.Add(snapshot);
            await db.SaveChangesAsync(Ct);
            var before = await ArchiveAsync(db);
            var key = await SnapshotPrimaryKeyAsync(db);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var photoId = Guid.NewGuid();
            var covers = new Mock<ICollectionCoverStore>();
            covers.Setup(c => c.ClaimAsync(owner, It.IsAny<Guid>(), "collection-covers/upload.jpg", Ct))
                .ReturnsAsync(new ClaimedCollectionCoverDto(photoId, "collection-covers/sealed/cover.jpg", "image/jpeg", 100));
            var store = Store(db, covers.Object);
            SavedCollectionDto privateRow;
            await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
            {
                privateRow = await store.CreateAsync(owner, new() { Title = "Owner only", Items = [Item("second"), Item("first")],
                    CoverUpload = new UploadedPhotoDto("cover.jpg", "image/jpeg", "collection-covers/upload.jpg", 100) }, Ct);
                privateRow = (await store.ReplaceItemsAsync(owner, privateRow.Id, new([Item("first"), Item("second")], privateRow.Revision), Ct))!;
                var published = await store.CreateAsync(owner, new() { Title = "Public library", Visibility = "public" }, Ct);
                await store.SetFeaturedAsync(owner, published.Id, Ct);
                await transaction.CommitAsync(Ct);
            }
            db.ChangeTracker.Clear();
            Assert.Equal("private", privateRow.Visibility);
            Assert.Equal(["first", "second"], (await store.GetAsync(owner, privateRow.Id, Ct))!.Items.Select(i => i.Address!.Slug));
            Assert.Null(await store.GetPublicAsync(privateRow.Id, Ct));
            Assert.False(await db.FavoriteCollections.AnyAsync(c => c.Id == privateRow.Id, Ct));
            Assert.Equal(snapshot.Id, (await db.FavoriteCollections.AsNoTracking().SingleAsync(Ct)).Id);
            Assert.Equal(["second", "first"], (await snapshots.GetAsync(snapshot.Id, Ct))!.Items.Select(i => i.Address!.Slug));
            Assert.Equal(key, await SnapshotPrimaryKeyAsync(db));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await ArchiveAsync(db))));
            Assert.Single(await db.SavedCollectionCoverUses.ToArrayAsync(Ct));
            var rollback = db.GetService<IMigrator>().GenerateScript(Migration, Previous, MigrationsSqlGenerationOptions.NoTransactions);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(rollback, Ct));
            Assert.Contains("Saved collection data exists", rejected.MessageText);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await ArchiveAsync(db))));
            await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
            {
                await store.DeleteForOwnerAsync(owner, Ct);
                await transaction.CommitAsync(Ct);
            }
            await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);
            Assert.Equal(key, await SnapshotPrimaryKeyAsync(db));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await ArchiveAsync(db))));
            Assert.Equal(snapshot.Id, (await snapshots.GetAsync(snapshot.Id, Ct))!.Id);
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task OwnerTransactionLockSerializesWritersAndStaleMetadataCannotOverwriteCommittedChange()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var connection = await LivePostgresServer.CreateDatabaseAsync("savedlibrarylocks", Ct);
        try
        {
            var owner = Guid.NewGuid(); SavedCollectionDto row;
            await using (var seed = Context(connection))
            {
                await seed.Database.MigrateAsync(Ct);
                await using var transaction = await seed.Database.BeginTransactionAsync(Ct);
                row = await Store(seed).CreateAsync(owner, new() { Title = "Before" }, Ct);
                await transaction.CommitAsync(Ct);
            }
            await using var first = Context(connection); await using var second = Context(connection);
            await using var firstTransaction = await first.Database.BeginTransactionAsync(Ct);
            await using var secondTransaction = await second.Database.BeginTransactionAsync(Ct);
            await Store(first).UpdateAsync(owner, row.Id, new() { Title = "Committed", Visibility = "private", ExpectedRevision = row.Revision }, Ct);
            var waiting = Store(second).UpdateAsync(owner, row.Id, new() { Title = "Stale", Visibility = "private", ExpectedRevision = row.Revision }, Ct);
            await LivePostgresServer.WaitForLockWaitsAsync(connection, "advisory", 1, Ct);
            await firstTransaction.CommitAsync(Ct);
            var conflict = await Assert.ThrowsAsync<DiscoveryException>(() => waiting);
            Assert.Equal("COLLECTION_CONFLICT", conflict.ErrorCode);
            await secondTransaction.RollbackAsync(Ct);
            await using var read = Context(connection);
            Assert.Equal("Committed", (await Store(read).GetAsync(owner, row.Id, Ct))!.Title);
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    private static async Task<DiscoveryFavorite[]> SeedPlacesAsync(ShopsDbContext db, Guid owner)
    {
        var first = new Roaster("First"); first.SetPublicSlug("first");
        var second = new Roaster("Second"); second.SetPublicSlug("second");
        db.Roasters.AddRange(first, second);
        var firstFavorite = new DiscoveryFavorite(owner, "roaster", first.Id);
        var secondFavorite = new DiscoveryFavorite(owner, "roaster", second.Id);
        db.DiscoveryFavorites.AddRange(firstFavorite, secondFavorite);
        await db.SaveChangesAsync(Ct);
        return [secondFavorite, firstFavorite];
    }

    private static Task<string> ArchiveAsync(ShopsDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object(
            'collections',(SELECT jsonb_agg(to_jsonb(c) ORDER BY c."Id") FROM "FavoriteCollections" c),
            'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY i."CollectionId",i."Position") FROM "FavoriteCollectionItems" i))::text AS "Value"
        """).SingleAsync(Ct);

    private static Task<string> SnapshotPrimaryKeyAsync(ShopsDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
        WHERE conrelid = '"FavoriteCollectionItems"'::regclass AND contype = 'p'
        """).SingleAsync(Ct);
}
