using System.Text.Json.Nodes;
using CoffeePeek.LegacyCheckInsMigration;
using CoffeePeek.Shops.Infrastructure.Tests.Persistence;
using CoffeePeek.Shops.Persistance.Migrations;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using NpgsqlTypes;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

// Minimal typed archive fixture plus production target DDL; not a full historical-schema/MinIO/Redis rehearsal.
[Trait("Category", LivePostgresServer.Category)]
public class LegacyReviewBackfillLiveTests
{
    public LegacyReviewBackfillLiveTests() => Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);

    [Fact]
    public async Task ActualPostgres_StagesResumeWithoutDuplicatesAndPreserveArchivesNativeEditsAndReportAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = new Dictionary<string, NpgsqlConnection>();
        var databases = new List<string>();
        try
        {
            foreach (var service in new[] { "Shops", "Moderation", "Account", "Media" })
            {
                var connectionString = await LivePostgresServer.CreateDatabaseAsync("backfill" + service.ToLowerInvariant(), ct);
                databases.Add(connectionString);
                db[service] = new NpgsqlConnection(connectionString);
                await db[service].OpenAsync(ct);
            }
            var inv = LegacyReviewBackfillTests.Fixture();
            var user = inv["Users"]![0]!.AsObject();
            user.Remove("Statistics_CheckInCount"); user["CheckInCount"] = 9; user["AddedShopsCount"] = 7;
            var reviewId = inv["Reviews"]![0]!["Id"]!.ToString();
            inv["CheckIns"]!.AsArray().Add(new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["ReviewId"] = reviewId,
                ["UserId"] = user["Id"]!.DeepClone(), ["ShopId"] = inv["Shops"]![0]!["Id"]!.DeepClone(),
                ["VisitedAt"] = "2024-12-30T00:00:00Z", ["CreatedAtUtc"] = "2024-12-31T00:00:00Z",
                ["Note"] = "Legacy 1", ["Rating_Place"] = 3, ["Rating_Service"] = 4, ["Rating_Coffee"] = 5 });
            inv["ReviewReports"]!.AsArray().Add(new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["ReviewId"] = reviewId,
                ["ReportedByUserId"] = user["Id"]!.DeepClone(), ["Text"] = "original report", ["Status"] = 2,
                ["CreatedAtUtc"] = "2025-01-02T00:00:00Z", ["ResolvedByAdminId"] = Guid.NewGuid().ToString(),
                ["ResolvedAtUtc"] = "2025-01-03T00:00:00Z" });
            inv["ReviewHelpfulVotes"]!.AsArray().Add(new JsonObject { ["ReviewId"] = reviewId,
                ["UserId"] = user["Id"]!.DeepClone(), ["CreatedAtUtc"] = "2025-01-02T00:00:00Z" });
            const string content = "\"Id\" uuid, \"UserId\" uuid, \"UserName\" text, \"Header\" text, \"Comment\" text, \"Rating_Place\" integer, \"Rating_Service\" integer, \"Rating_Coffee\" integer, \"CreatedAtUtc\" timestamptz, \"UpdatedAtUtc\" timestamptz, \"IsSoftDelete\" boolean";
            var sources = new[] {
                ("Shops", "Shops", "\"Id\" uuid"),
                ("Shops", "Reviews", content + ", \"CoffeeShopId\" uuid, \"ModerationReviewId\" uuid, \"IsRemovedByAdmin\" boolean, \"HelpfulCount\" integer"),
                ("Shops", "CheckIns", "\"Id\" uuid, \"UserId\" uuid, \"ShopId\" uuid, \"ReviewId\" uuid, \"VisitedAt\" timestamptz, \"CreatedAtUtc\" timestamptz, \"Note\" text, \"Rating_Place\" integer, \"Rating_Service\" integer, \"Rating_Coffee\" integer"),
                ("Shops", "ReviewHelpfulVotes", "\"ReviewId\" uuid, \"UserId\" uuid, \"CreatedAtUtc\" timestamptz"),
                ("Shops", "ReviewReports", "\"Id\" uuid, \"ReviewId\" uuid, \"ReportedByUserId\" uuid, \"Text\" text, \"Status\" integer, \"CreatedAtUtc\" timestamptz, \"ResolvedByAdminId\" uuid, \"ResolvedAtUtc\" timestamptz"),
                ("Moderation", "ModerationReviews", content + ", \"ShopId\" uuid, \"ModerationStatus\" integer, \"RejectedReason\" text, \"ModeratedBy\" uuid, \"ModeratedAt\" timestamptz"),
                ("Account", "Users", "\"Id\" uuid, \"Username\" text, \"CheckInCount\" integer, \"AddedShopsCount\" integer") };
            foreach (var (service, table, columns) in sources)
            {
                await CreateArchiveAsync(db[service], table, columns, inv[table]!.AsArray(), ct);
                inv[table] = await LegacyReviewBackfill.ReadAsync(db[service], table, "", ct);
            }
            await SqlAsync(db["Shops"], "ALTER TABLE \"Shops\" ADD PRIMARY KEY (\"Id\")", ct);
            foreach (var sql in new CanonicalCheckInsOnMain().UpOperations.OfType<SqlOperation>())
                await SqlAsync(db["Shops"], sql.Sql, ct);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
            var moderationMigration = await File.ReadAllTextAsync(Path.Combine(root, "CoffeeShop.Moderation.Persistence/Migrations/20261007133453_CanonicalCheckInSnapshotsOnMain.cs"), ct);
            await SqlAsync(db["Moderation"], moderationMigration.Split("\"\"\"")[1], ct);
            var native = LegacyReviewBackfill.Map(inv)[0]!["Target"]!.AsObject().DeepClone().AsObject();
            var nativeId = Guid.NewGuid().ToString();
            native["Id"] = nativeId; native["Note"] = "native visit"; native["RowVersion"] = Guid.NewGuid().ToString();
            await using (var insert = LegacyReviewBackfill.InsertCommand("CanonicalCheckIns", native, db["Shops"]))
                await insert.ExecuteNonQueryAsync(ct);
            inv["CanonicalCheckIns"] = await LegacyReviewBackfill.ReadAsync(db["Shops"], "CanonicalCheckIns", "", ct);
            var manifest = LegacyReviewBackfillTests.Manifest(inv);
            // Explicit fixture bypass: no Redis or historical photos; this is only the PostgreSQL data-stage test.
            manifest["CacheInvalidationPending"] = false;
            var reportHash = LegacyReviewBackfill.Hash(inv["ReviewReports"]!);
            await LegacyReviewBackfill.ApplyVisitsAsync(manifest, db, ct);
            Assert.Empty(await LegacyReviewBackfill.ReadAsync(db["Moderation"], "ModerationCheckIns", "", ct));
            foreach (var entry in LegacyReviewBackfill.ReadyEntries(manifest))
            {
                var staged = await LegacyReviewBackfill.FindAsync(db["Shops"], "CanonicalCheckIns", entry["Target"]!["Id"]!.ToString(), ct);
                Assert.Equal(0, staged!["Visibility"]!.GetValue<int>());
                Assert.Null(staged["ApprovedRevision"]);
            }
            await LegacyReviewBackfill.ApplyVisitsAsync(manifest, db, ct); // Resume after committed visits, before snapshots.
            await RemainingStagesAsync(manifest, db, ct);
            var targets = await LegacyReviewBackfill.ReadAsync(db["Shops"], "CanonicalCheckIns", "", ct);
            Assert.Equal(6, targets.Count);
            Assert.Single(await LegacyReviewBackfill.ReadAsync(db["Shops"], "CheckInHelpfulVotes", "", ct));
            Assert.Equal(5, (await LegacyReviewBackfill.ReadAsync(db["Moderation"], "ModerationCheckIns", "", ct)).Count);
            var imported = targets.OfType<JsonObject>().Single(r => r["Id"]!.ToString() == reviewId);
            Assert.Equal(1, imported["HelpfulCount"]!.GetValue<int>()); Assert.Equal(2, imported["ModerationState"]!.GetValue<int>());
            Assert.Equal(DateTimeOffset.Parse("2024-12-30T00:00:00Z"), DateTimeOffset.Parse(imported["VisitedAt"]!.ToString()));
            var account = (await LegacyReviewBackfill.ReadAsync(db["Account"], "Users", "", ct))[0]!;
            Assert.Equal(6, account["CheckInCount"]!.GetValue<int>()); Assert.Equal(7, account["AddedShopsCount"]!.GetValue<int>());
            Assert.True((await LegacyReviewBackfill.ReconcileAsync(manifest, db, ct))["ReleaseReady"]!.GetValue<bool>());
            await LegacyReviewBackfill.ApplyVisitsAsync(manifest, db, ct);
            await RemainingStagesAsync(manifest, db, ct);
            Assert.True((await LegacyReviewBackfill.ReconcileAsync(manifest, db, ct))["ReleaseReady"]!.GetValue<bool>());
            Assert.Equal(6, (await LegacyReviewBackfill.ReadAsync(db["Shops"], "CanonicalCheckIns", "", ct)).Count);
            await using (var edit = new NpgsqlCommand("UPDATE \"CanonicalCheckIns\" SET \"Note\"=@note, \"ContentRevision\"=\"ContentRevision\"+1, \"Visibility\"=0, \"ApprovedRevision\"=NULL WHERE \"Id\"=ANY(@ids)", db["Shops"]))
            {
                edit.Parameters.AddWithValue("note", "later author edit");
                edit.Parameters.AddWithValue("ids", new[] { Guid.Parse(reviewId), Guid.Parse(nativeId) });
                await edit.ExecuteNonQueryAsync(ct);
            }
            await LegacyReviewBackfill.ApplyVisitsAsync(manifest, db, ct);
            await RemainingStagesAsync(manifest, db, ct);
            Assert.Equal("conflict", manifest["Mappings"]!.AsArray().Single(e => e!["SourceId"]!.ToString() == reviewId)!["Outcome"]!.ToString());
            foreach (var id in new[] { reviewId, nativeId })
                Assert.Equal("later author edit", (await LegacyReviewBackfill.FindAsync(db["Shops"], "CanonicalCheckIns", id, ct))!["Note"]!.ToString());
            Assert.False((await LegacyReviewBackfill.ReconcileAsync(manifest, db, ct))["ReleaseReady"]!.GetValue<bool>());
            foreach (var (service, table, _) in sources.Where(s => s.Item2 != "Users"))
                Assert.Equal(LegacyReviewBackfill.Hash(inv[table]!), LegacyReviewBackfill.Hash(await LegacyReviewBackfill.ReadAsync(db[service], table, "", ct)));
            Assert.Equal(reportHash, LegacyReviewBackfill.Hash(await LegacyReviewBackfill.ReadAsync(db["Shops"], "ReviewReports", "", ct)));
        }
        finally
        {
            try { foreach (var connection in db.Values) await connection.DisposeAsync(); }
            finally { foreach (var connectionString in databases) await LivePostgresServer.DropDatabaseAsync(connectionString); }
        }
    }

    private static async Task RemainingStagesAsync(JsonObject manifest, IReadOnlyDictionary<string, NpgsqlConnection> db, CancellationToken ct)
    {
        await LegacyReviewBackfill.ApplySnapshotsAsync(manifest, db, ct);
        await LegacyReviewBackfill.ApplyVotesAsync(manifest, db, ct);
        await LegacyReviewBackfill.PublishAsync(manifest, db, ct);
        await LegacyReviewBackfill.ReconcileCountsAsync(manifest, db, ct);
    }

    private static async Task CreateArchiveAsync(NpgsqlConnection connection, string table, string columns, JsonArray rows, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"CREATE TABLE {new NpgsqlCommandBuilder().QuoteIdentifier(table)} AS SELECT * FROM jsonb_to_recordset(@rows) AS r({columns})", connection);
        command.Parameters.Add("rows", NpgsqlDbType.Jsonb).Value = rows.ToJsonString();
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task SqlAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
