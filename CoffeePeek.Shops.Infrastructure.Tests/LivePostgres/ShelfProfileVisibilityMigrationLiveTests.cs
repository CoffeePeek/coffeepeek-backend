using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Shelf;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShelfProfileVisibilityMigrationLiveTests
{
    [Fact]
    public async Task UpgradeUsesPublicDefaultAndOptOutPersistsAndRollbackPreservesBags()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("shelfprofileupgrade", ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
            const string previous = "20261103135540_AddSavedCollectionLibrary";
            await db.GetService<IMigrator>().MigrateAsync(previous, ct);
            var owner = Guid.NewGuid();
            var bag = ShelfBag.Create(Guid.NewGuid(), owner, new(null, "Existing private coffee", null, 250, null, null, null), null, DateTime.UtcNow);
            new ShelfLineup(owner, []).AddToFront(bag);
            db.ShelfBags.Add(bag); await db.SaveChangesAsync(ct);
            await db.Database.MigrateAsync(ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var store = new ShelfProfileStore(db, Mock.Of<IShelfCatalogLookup>());
            Assert.True((await store.VisibilityAsync(owner, ct)).IsVisible);
            Assert.Equal(bag.Id, Assert.Single((await store.PublicAsync(owner, "author", ct))!.Items).Id);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await store.SetVisibilityAsync(owner, false, ct);
                await transaction.CommitAsync(ct);
            }
            db.ChangeTracker.Clear();
            Assert.False((await store.VisibilityAsync(owner, ct)).IsVisible);
            Assert.Null(await store.PublicAsync(owner, "author", ct));
            await db.GetService<IMigrator>().MigrateAsync(previous, ct);
            Assert.Equal("Existing private coffee", (await db.ShelfBags.AsNoTracking().SingleAsync(ct)).Name);
            await db.Database.MigrateAsync(ct);
            Assert.True((await store.VisibilityAsync(owner, ct)).IsVisible);
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }
}
