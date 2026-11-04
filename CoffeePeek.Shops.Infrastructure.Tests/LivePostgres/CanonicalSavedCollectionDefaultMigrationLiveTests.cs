using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.DiscoveryAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Discovery;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CanonicalSavedCollectionDefaultMigrationLiveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UpgradeAdoptsExistingPublicTitleWithoutChangingVisibilityAndEnforcesOneActiveDefault()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var connection = await LivePostgresServer.CreateDatabaseAsync("canonicaldefault", Ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                .UseNpgsql(connection).Options);
            await db.Database.MigrateAsync("20261104065015_AddShelfProfileVisibility", Ct);
            var owner = Guid.NewGuid(); var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SavedCollections" ("Id", "OwnerId", "Title", "Visibility", "Revision", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ({id}, {owner}, 'Избранное', 'public', 1, now(), now())
                """, Ct);
            var legacySchema = await LegacySchemaAsync(db);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(legacySchema, await LegacySchemaAsync(db));
            var store = new SavedCollectionsStore(db, Mock.Of<IPublicAddressService>(),
                Mock.Of<ICollectionCoverStore>(), Mock.Of<IMessageBus>());
            await using (var transaction = await db.Database.BeginTransactionAsync(Ct))
            {
                var canonical = await store.CreateAsync(owner, new CreateSavedCollectionRequest
                    { Title = "Избранное", IsDefault = true }, Ct);
                Assert.Equal(id, canonical.Id);
                Assert.Equal("public", canonical.Visibility);
                Assert.True(canonical.IsDefault);
                await transaction.CommitAsync(Ct);
            }
            db.ChangeTracker.Clear();
            var duplicate = SavedCollection.CreateList(owner, Guid.NewGuid(), "Other", null, "private");
            duplicate.MarkDefault(); db.SavedCollections.Add(duplicate);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
            Assert.Single(await db.SavedCollections.AsNoTracking().Where(c => c.OwnerId == owner && c.IsDefault).ToArrayAsync(Ct));
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    private static Task<string> LegacySchemaAsync(ShopsDbContext db) => db.Database.SqlQueryRaw<string>("""
        SELECT string_agg(table_name || '.' || column_name || ':' || data_type, ',' ORDER BY table_name, ordinal_position) AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name IN ('FavoriteCollections', 'FavoriteCollectionItems')
        """).SingleAsync(Ct);
}
