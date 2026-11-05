using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Shelf;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShelfPublicDefaultMigrationLiveTests
{
    [Fact]
    public async Task MigrationChangesOnlyTheDefaultAndFirstExplicitOptOutPersists()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("shelfpublicdefault", ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
            const string previous = "20261104105519_AddCanonicalSavedCollectionDefault";
            await db.GetService<IMigrator>().MigrateAsync(previous, ct);
            var hidden = Guid.NewGuid(); var visible = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ShelfProfilePreferences" ("OwnerId", "IsVisible")
                VALUES ({hidden}, false), ({visible}, true)
                """, ct);

            await db.Database.MigrateAsync(ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var store = new ShelfProfileStore(db, Mock.Of<IShelfCatalogLookup>());
            Assert.False((await store.VisibilityAsync(hidden, ct)).IsVisible);
            Assert.True((await store.VisibilityAsync(visible, ct)).IsVisible);

            var defaultOwner = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ShelfProfilePreferences\" (\"OwnerId\") VALUES ({defaultOwner})", ct);
            Assert.True((await store.VisibilityAsync(defaultOwner, ct)).IsVisible);

            // Inserting false must override the true SQL default, even on the owner's first write.
            var firstOptOut = Guid.NewGuid();
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await store.SetVisibilityAsync(firstOptOut, false, ct);
                await transaction.CommitAsync(ct);
            }
            db.ChangeTracker.Clear();
            Assert.False((await store.VisibilityAsync(firstOptOut, ct)).IsVisible);
            Assert.False((await db.ShelfProfilePreferences.AsNoTracking().SingleAsync(p => p.OwnerId == firstOptOut, ct)).IsVisible);

            await db.GetService<IMigrator>().MigrateAsync(previous, ct);
            var rollbackOwner = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ShelfProfilePreferences\" (\"OwnerId\") VALUES ({rollbackOwner})", ct);
            Assert.False((await store.VisibilityAsync(rollbackOwner, ct)).IsVisible);
            Assert.True((await store.VisibilityAsync(defaultOwner, ct)).IsVisible);
            await db.Database.MigrateAsync(ct);
            Assert.False((await store.VisibilityAsync(firstOptOut, ct)).IsVisible);
            Assert.False((await store.VisibilityAsync(rollbackOwner, ct)).IsVisible);
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    [Fact]
    public async Task PublicationScriptIncludesMissingAndHiddenPreferencesAndCanBeRepeated()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("shelfpublication", ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
            await db.Database.MigrateAsync(ct);
            var missing = Guid.NewGuid(); var hidden = Guid.NewGuid(); var archiveOnly = Guid.NewGuid();
            var emptyHidden = Guid.NewGuid(); var alreadyVisible = Guid.NewGuid();
            var first = Bag(missing, "First"); var second = Bag(missing, "Second");
            var lineup = new ShelfLineup(missing, []); lineup.AddToFront(first); lineup.AddToFront(second);
            var hiddenBag = Bag(hidden, "Hidden"); new ShelfLineup(hidden, []).AddToFront(hiddenBag);
            var archivedBag = Bag(archiveOnly, "Archived");
            var archive = new ShelfLineup(archiveOnly, []); archive.AddToFront(archivedBag);
            archive.Finish(archivedBag, 5, true, DateTime.UtcNow);
            db.ShelfBags.AddRange(first, second, hiddenBag, archivedBag);
            foreach (var owner in new[] { hidden, emptyHidden, alreadyVisible })
            {
                var preference = new ShelfProfilePreference(owner);
                preference.SetVisibility(owner == alreadyVisible);
                db.ShelfProfilePreferences.Add(preference);
            }
            await db.SaveChangesAsync(ct);

            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "publish-all-shelves.sql"), ct);
            await db.Database.OpenConnectionAsync(ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            Assert.Equal(4L, await command.ExecuteScalarAsync(ct));
            Assert.Equal(0L, await command.ExecuteScalarAsync(ct));
            db.ChangeTracker.Clear();
            var preferences = await db.ShelfProfilePreferences.AsNoTracking().ToArrayAsync(ct);
            Assert.Equal(5, preferences.Length);
            Assert.All(preferences, p => Assert.True(p.IsVisible));
            Assert.Equal(4, await db.ShelfBags.CountAsync(ct));
            Assert.Equal(ShelfBagStatus.Finished, (await db.ShelfBags.AsNoTracking().SingleAsync(b => b.Id == archivedBag.Id, ct)).Status);
            var store = new ShelfProfileStore(db, Mock.Of<IShelfCatalogLookup>());
            Assert.Equal(hiddenBag.Id, Assert.Single((await store.PublicAsync(hidden, "author", ct))!.Items).Id);
            Assert.Empty((await store.PublicAsync(archiveOnly, "archived", ct))!.Items);
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                await store.SetVisibilityAsync(hidden, false, ct);
                await transaction.CommitAsync(ct);
            }
            Assert.Null(await store.PublicAsync(hidden, "author", ct));
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    private static ShelfBag Bag(Guid owner, string name) =>
        ShelfBag.Create(Guid.NewGuid(), owner, new(null, name, null, 250, null, null, null), null, DateTime.UtcNow);
}
