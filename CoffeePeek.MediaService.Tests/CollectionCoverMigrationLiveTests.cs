using System.Text.Json.Nodes;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CoffeePeek.MediaService.Tests;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CollectionCoverMigrationLiveTests
{
    private const string Previous = "20261029100047_AddOwnershipEvidenceBindings";
    private const string Migration = "20261103134700_AddCollectionCovers";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PopulatedUpgradePreservesExistingPhotoMetadataAndRollbackProtectsNewCoverBindings()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var connection = await LivePostgresServer.CreateDatabaseAsync("mediacoverupgrade", Ct);
        try
        {
            await using var db = new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(connection).Options);
            await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);
            var historicalId = Guid.NewGuid(); var owner = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Photos" ("Id","FileName","ContentType","StorageKey","SizeBytes","UploadedAt","Status","BucketType","OwnerType","OwnerId")
                VALUES ({historicalId},'old.jpg','image/jpeg','shops/old.jpg',100,{DateTime.UtcNow},'Confirmed','Shop','Shop',{owner})
                """, Ct);
            var before = await HistoricalAsync(db, historicalId);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var historical = await db.Photos.SingleAsync(p => p.Id == historicalId, Ct);
            Assert.Null(historical.CollectionId);
            Assert.Null(historical.CollectionCoverValidatedAtUtc);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await HistoricalAsync(db, historicalId))));
            var cover = PhotoMetadata.Pending("cover.jpg", "image/jpeg", "collection-covers/sealed/cover.jpg", 100,
                BucketType.CheckIn, OwnerType.Shop, owner);
            cover.CollectionId = Guid.NewGuid(); cover.CollectionCoverValidatedAtUtc = DateTime.UtcNow;
            db.Photos.Add(cover); await db.SaveChangesAsync(Ct);
            var rollback = db.GetService<IMigrator>().GenerateScript(Migration, Previous, MigrationsSqlGenerationOptions.NoTransactions);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(rollback, Ct));
            Assert.Contains("Collection cover bindings exist", rejected.MessageText);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await HistoricalAsync(db, historicalId))));
            db.Photos.Remove(cover); await db.SaveChangesAsync(Ct);
            await db.GetService<IMigrator>().MigrateAsync(Previous, Ct);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(await HistoricalAsync(db, historicalId))));
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    private static Task<string> HistoricalAsync(MediaDbContext db, Guid id) => db.Database.SqlQuery<string>($"""
        SELECT (to_jsonb(p)-'CollectionId'-'CollectionCoverValidatedAtUtc')::text AS "Value"
        FROM "Photos" p WHERE p."Id"={id}
        """).SingleAsync(Ct);
}
