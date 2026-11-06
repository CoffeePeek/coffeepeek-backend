using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInPhotoMigrationLiveTests : IClassFixture<CheckInPhotoMigrationDatabase>
{
    private readonly CheckInPhotoMigrationDatabase fixture;
    public CheckInPhotoMigrationLiveTests(CheckInPhotoMigrationDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task UpgradePreservesArchiveAndIdentityAndRepeatedScriptDoesNotDuplicatePhotos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.Context(false);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(fixture.ArchiveBefore, await CheckInPhotoMigrationDatabase.ArchiveAsync(db, ct));
        Assert.Equal(3, await db.CheckInPhotos.CountAsync(ct));
        var visit = await db.CheckIns.Include(c => c.ShopPhotos).SingleAsync(c => c.Id == fixture.VisitId, ct);
        Assert.Equal(2, visit.ShopPhotos.Count);
        Assert.True(visit.IsPubliclyVisible);
        foreach (var photo in visit.ShopPhotos)
        {
            var old = await db.ShopPhotos.AsNoTracking().SingleAsync(p => p.Id == photo.Id, ct);
            Assert.Equal(old.StorageKey, photo.StorageKey);
            Assert.Equal(old.OwnerId, photo.OwnerId);
            Assert.Equal(old.CreatedAtUtc, photo.CreatedAtUtc);
            Assert.Equal(old.SortIndex, photo.SortIndex);
            Assert.Equal(fixture.VisitId, photo.CanonicalCheckInId);
        }
        var projection = await new CheckInQueries(db, Moq.Mock.Of<CoffeePeek.Shops.Application.Abstractions.IAuthorVisibilityLookup>()).GetUnifiedByIdAsync(visit.Id, null, ct);
        Assert.NotNull(projection);
        Assert.Equal(2, projection.Photos.Length);
        Assert.True(await new QueryCheckInRepository(db).ArePhotoKeysUsedAsync([visit.ShopPhotos.First().StorageKey], ct));
        var script = db.GetService<IMigrator>().GenerateScript(CheckInPhotoMigrationDatabase.PreviousMigration,
            "20261009081229_SeparateCommunityCheckInPhotos", MigrationsSqlGenerationOptions.Idempotent);
        await db.Database.ExecuteSqlRawAsync(script, ct);
        await db.Database.ExecuteSqlRawAsync(script, ct);
        Assert.Equal(3, await db.CheckInPhotos.CountAsync(ct));
        Assert.Equal(fixture.ArchiveBefore, await CheckInPhotoMigrationDatabase.ArchiveAsync(db, ct));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("gallery")]
    [InlineData("duplicate")]
    [InlineData("limit")]
    [InlineData("metadata")]
    public async Task ContradictoryLegacyPhotosAbortUpgradeWithoutChangingArchive(string violation)
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await LivePostgresServer.CreateDatabaseAsync("photopreflight", ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connectionString).Options);
            await db.GetService<IMigrator>().MigrateAsync(CheckInPhotoMigrationDatabase.PreviousMigration, ct);
            var visitId = Guid.NewGuid();
            await CheckInPhotoMigrationDatabase.SeedAsync(db, visitId, ct);
            var sql = violation switch
            {
                "owner" => "UPDATE \"ShopPhotos\" SET \"OwnerId\"='00000000-0000-0000-0000-000000000001' WHERE \"CanonicalCheckInId\" IS NOT NULL",
                "gallery" => "UPDATE \"ShopPhotos\" SET \"CoffeeShopId\"=(SELECT \"ShopId\" FROM \"CanonicalCheckIns\" LIMIT 1) WHERE \"CanonicalCheckInId\" IS NOT NULL",
                "duplicate" => "UPDATE \"ShopPhotos\" SET \"StorageKey\"='duplicate' WHERE \"CanonicalCheckInId\" IS NOT NULL",
                "metadata" => "UPDATE \"ShopPhotos\" SET \"SizeBytes\"=0 WHERE \"CanonicalCheckInId\" IS NOT NULL",
                _ => """
                    INSERT INTO "ShopPhotos" ("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId", "SortIndex", "CanonicalCheckInId", "CreatedAtUtc")
                    SELECT gen_random_uuid(), 'photo.jpg', 'image/jpeg', gen_random_uuid()::text, 42, c."UserId", n, c."Id", now()
                    FROM "CanonicalCheckIns" c CROSS JOIN generate_series(0,5) n WHERE c."Id"=@visit
                    """
            };
            await db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("visit", visitId)], ct);
            var before = await CheckInPhotoMigrationDatabase.ArchiveAsync(db, ct);
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync(ct));
            Assert.Contains("explicit repair", error.MessageText);
            Assert.Equal(before, await CheckInPhotoMigrationDatabase.ArchiveAsync(db, ct));
            Assert.DoesNotContain("20261009081229_SeparateCommunityCheckInPhotos", await db.Database.GetAppliedMigrationsAsync(ct));
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connectionString); }
    }
}
