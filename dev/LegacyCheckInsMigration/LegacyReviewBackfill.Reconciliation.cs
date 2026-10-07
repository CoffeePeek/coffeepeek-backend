using System.Text.Json.Nodes;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StackExchange.Redis;

namespace CoffeePeek.LegacyCheckInsMigration;

public static partial class LegacyReviewBackfill
{
    private static readonly Dictionary<string, string[]> SourceTables = new()
    {
        ["Shops"] = ["Reviews", "CheckIns", "ReviewHelpfulVotes", "ReviewReports", "Shops", "CoffeeDrinkDefinitions", "ShopPhotos"],
        ["Moderation"] = ["ModerationReviews", "ShopPhotos"],
        ["Account"] = ["Users"],
        ["Media"] = ["Photos"]
    };

    public static async Task<JsonObject> InventoryAsync(IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        var inventory = new JsonObject();
        foreach (var (service,tables) in SourceTables)
        foreach (var table in tables)
        {
            var key = service == "Moderation" && table == "ShopPhotos" ? "ModerationPhotos" : table == "CoffeeDrinkDefinitions" ? "Drinks" : table;
            var condition = table == "ShopPhotos" ? (service == "Shops" ? " WHERE \"CanonicalCheckInId\" IS NULL" : " WHERE \"ModerationCheckInId\" IS NULL") : "";
            inventory[key] = await ReadAsync(db[service], table, condition, ct);
        }
        inventory["CanonicalCheckIns"] = await ReadAsync(db["Shops"], "CanonicalCheckIns", "", ct);
        inventory["ModerationCheckIns"] = await ReadAsync(db["Moderation"], "ModerationCheckIns", "", ct);
        inventory["CheckInHelpfulVotes"] = await ReadAsync(db["Shops"], "CheckInHelpfulVotes", "", ct);
        // All gallery photo tables are included in exact-key collision checks, not just review photos.
        var gallery = new JsonArray();
        foreach (var service in new[] { "Shops", "Moderation" })
        {
            await using var tables = new NpgsqlCommand("SELECT table_name FROM information_schema.columns WHERE table_schema='public' AND column_name='StorageKey' ORDER BY table_name",db[service]);
            var names = new List<string>();
            await using (var reader = await tables.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
            foreach (var table in names)
            foreach (var row in (await ReadAsync(db[service], table, "", ct)).OfType<JsonObject>())
                gallery.Add(new JsonObject { ["Service"] = service, ["Table"] = table, ["Row"] = row.DeepClone() });
        }
        inventory["Gallery"] = gallery;
        return inventory;
    }

    public static async Task<JsonArray> ReadAsync(NpgsqlConnection connection, string table, string condition, CancellationToken ct)
    {
        // table comes only from the fixed list or PostgreSQL's schema catalog.
        var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(table);
        await using var command = new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM {quoted} t{condition} ORDER BY to_jsonb(t)::text",connection);
        var rows = new JsonArray();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(JsonNode.Parse(reader.GetString(0)));
        return rows;
    }

    public static async Task ValidateSchemaAsync(IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        var required = new Dictionary<string, Dictionary<string,string[]>> {
            ["Shops"] = new() {
                ["Reviews"] = ["Id","UserId","CoffeeShopId","UserName","Header","Comment","ModerationReviewId","IsSoftDelete","IsRemovedByAdmin","HelpfulCount","CreatedAtUtc","Rating_Place","Rating_Service","Rating_Coffee"],
                ["CheckIns"] = ["Id","UserId","ShopId","ReviewId","VisitedAt","Note","Rating_Place","Rating_Service","Rating_Coffee"],
                ["ReviewHelpfulVotes"] = ["ReviewId","UserId","CreatedAtUtc"], ["ReviewReports"] = ["Id","ReviewId"],
                ["CanonicalCheckIns"] = ["Id","Note","ContentRevision","RowVersion","ApprovedRevision","DeletedAtUtc","FirstPublishedAtUtc"],
                ["ShopPhotos"] = ["Id","ReviewId","CheckInId","CanonicalCheckInId","OwnerId","StorageKey","SortIndex"] },
            ["Moderation"] = new() {
                ["ModerationReviews"] = ["Id","UserId","ShopId","ModerationStatus","Comment","ModeratedBy","ModeratedAt","RejectedReason","IsSoftDelete"],
                ["ModerationCheckIns"] = ["Id","CheckInId","ContentRevision","DecisionSequence"],
                ["ShopPhotos"] = ["Id","ModerationReviewId","ModerationCheckInId","CheckInPhotoId","OwnerId","SortIndex"] },
            ["Account"] = new() { ["Users"] = ["Id","CheckInCount","Username"] },
            ["Media"] = new() { ["Photos"] = ["Id","CheckInId","OwnerId","OwnerType","BucketType","OriginalStorageKey","StorageKey","SizeBytes","Status"] }
        };
        foreach (var (service,tables) in required)
        foreach (var (table,columns) in tables)
        {
            await using var command = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name=@table",db[service]);
            command.Parameters.AddWithValue("table",table);
            var actual = new HashSet<string>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) actual.Add(reader.GetString(0));
            if (columns.Any(c => !actual.Contains(c))) throw new InvalidOperationException($"Unsupported schema: {service}.{table}.");
        }
    }

    public static void Save(string path, JsonObject manifest)
    {
        var temporary = path + ".writing";
        File.WriteAllText(temporary,manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary,path,true);
    }

    public static async Task ApplyVisitsAsync(JsonObject manifest, IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        foreach (var entry in manifest["Mappings"]!.AsArray().OfType<JsonObject>().Where(e => e["Target"] != null))
        {
            var final = entry["Target"]!.AsObject();
            var stage = Stage(final);
            var current = await FindAsync(db["Shops"],"CanonicalCheckIns",Text(final,"Id")!,ct);
            var baseline = Rows(manifest["Inventory"]!.AsObject(),"CanonicalCheckIns").Any(r => Text(r,"Id") == Text(final,"Id"));
            if (baseline) { entry["Outcome"] = "conflict"; entry["Reason"] = "pre-existing native canonical UUID"; continue; }
            if (current != null && !Matches(final,current) && !Matches(stage,current))
            { entry["Outcome"] = "conflict"; entry["Reason"] = "imported target changed; never overwrite later edits"; continue; }
            await using var tx = await db["Shops"].BeginTransactionAsync(ct);
            if (current == null)
            {
                await using var insert = InsertCommand("CanonicalCheckIns",stage,db["Shops"],tx);
                if (await insert.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Visit insertion collision.");
            }
            await tx.CommitAsync(ct);
            entry["Outcome"] = current == null ? "mapped" : "already-present";
        }
    }

    public static async Task ApplySnapshotsAsync(JsonObject manifest, IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        foreach (var entry in ReadyEntries(manifest))
        {
            await using var tx = await db["Moderation"].BeginTransactionAsync(ct);
            foreach (var snapshot in entry["Snapshots"]!.AsArray().OfType<JsonObject>())
            {
                var current = await FindAsync(db["Moderation"],"ModerationCheckIns",Text(snapshot,"Id")!,ct,tx);
                if (current != null && !Matches(snapshot,current)) throw new InvalidOperationException("Snapshot identity/content conflict.");
                if (current == null)
                {
                    await using var insert = InsertCommand("ModerationCheckIns",snapshot,db["Moderation"],tx);
                    if (await insert.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Snapshot revision collision.");
                }
            }
            await tx.CommitAsync(ct);
        }
    }

    public static IEnumerable<JsonObject> ReadyEntries(JsonObject manifest) =>
        manifest["Mappings"]!.AsArray().OfType<JsonObject>().Where(e => e["Target"] != null && Text(e,"Outcome") is "mapped" or "already-present");

    public static async Task<JsonObject?> FindAsync(NpgsqlConnection connection, string table, string id, CancellationToken ct, NpgsqlTransaction? tx = null)
    {
        var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(table);
        await using var command = new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM {quoted} t WHERE \"Id\"=@id",connection,tx);
        command.Parameters.AddWithValue("id",Guid.Parse(id));
        return await command.ExecuteScalarAsync(ct) is string value ? JsonNode.Parse(value)!.AsObject() : null;
    }

    public static async Task PublishAsync(JsonObject manifest, IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        foreach (var entry in ReadyEntries(manifest))
        {
            if (Flag(entry,"PhotosUnresolved")) continue;
            var row = entry["Target"]!.AsObject();
            var current = await FindAsync(db["Shops"],"CanonicalCheckIns",Text(row,"Id")!,ct);
            if (current != null && Matches(row,current)) continue;
            if (current == null || !Matches(Stage(row),current)) throw new InvalidOperationException("Staged visit changed before publication.");
            await using var tx = await db["Shops"].BeginTransactionAsync(ct);
            var fields = new[] { "Visibility", "ModerationState", "ApprovedRevision", "FirstPublishedAtUtc" };
            var set = string.Join(", ",fields.Select(f => $"\"{f}\"=r.\"{f}\""));
            await using var command = new NpgsqlCommand($"UPDATE \"CanonicalCheckIns\" c SET {set} FROM jsonb_populate_record(NULL::\"CanonicalCheckIns\",@row) r WHERE c.\"Id\"=r.\"Id\" AND c.\"RowVersion\"=r.\"RowVersion\" AND c.\"Visibility\"=0 AND c.\"ModerationState\"=0",db["Shops"],tx);
            command.Parameters.Add("row",NpgsqlDbType.Jsonb).Value = row.ToJsonString();
            if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Publication guard failed.");
            await tx.CommitAsync(ct);
        }
    }

    public static void PrepareVotes(JsonObject manifest)
    {
        var inventory = manifest["Inventory"]!.AsObject();
        var outcomes = new JsonArray();
        var votes = new JsonArray();
        foreach (var vote in Rows(inventory,"ReviewHelpfulVotes"))
        {
            var id = Text(vote,"ReviewId")!;
            var entry = manifest["Mappings"]!.AsArray().OfType<JsonObject>().SingleOrDefault(e => Text(e,"SourceTable") == "Reviews" && Text(e,"SourceId") == id);
            var known = Rows(inventory,"Users").Any(u => Text(u,"Id") == Text(vote,"UserId"));
            if (entry?["Target"] == null || !known) { outcomes.Add(Outcome("ReviewHelpfulVotes",id+"/"+Text(vote,"UserId"),"unresolved","missing mapped visit/voter")); continue; }
            votes.Add(new JsonObject { ["CheckInId"] = id, ["UserId"] = Text(vote,"UserId"), ["CreatedAtUtc"] = vote["CreatedAtUtc"]!.DeepClone() });
            var mapped = Outcome("ReviewHelpfulVotes",id+"/"+Text(vote,"UserId"),"mapped",null); mapped["SourceHash"] = Hash(vote); outcomes.Add(mapped);
        }
        var reports = new JsonArray();
        foreach (var report in Rows(inventory,"ReviewReports"))
        {
            var targetId = Text(report,"ReviewId")!;
            var exists = manifest["Mappings"]!.AsArray().OfType<JsonObject>().Any(e => e["Target"] is JsonObject t && Text(t,"Id") == targetId)
                || Rows(inventory,"CanonicalCheckIns").Any(t => Text(t,"Id") == targetId);
            var outcome = Outcome("ReviewReports",Text(report,"Id")!,exists ? "already-present" : "unresolved",exists ? null : "missing canonical complaint target");
            outcome["SourceHash"] = Hash(report); outcome["TargetId"] = targetId; reports.Add(outcome);
        }
        manifest["ReportOutcomes"] = reports;

        manifest["Votes"] = votes; manifest["VoteOutcomes"] = outcomes;
        foreach (var entry in manifest["Mappings"]!.AsArray().OfType<JsonObject>().Where(e => e["Target"] != null))
        {
            var target = entry["Target"]!.AsObject();
            target["HelpfulCount"] = votes.Count(v => v!["CheckInId"]!.ToString() == Text(target,"Id")); entry["TargetHash"] = Hash(target);
            var original = Rows(inventory,"Reviews").SingleOrDefault(r => Text(r,"Id") == Text(target,"Id"));
            entry["OriginalHelpfulCount"] = original?["HelpfulCount"]?.DeepClone();
        }
    }

    public static async Task ApplyVotesAsync(JsonObject manifest, IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        await using var tx = await db["Shops"].BeginTransactionAsync(ct);
        foreach (var vote in manifest["Votes"]!.AsArray().OfType<JsonObject>())
        {
            if (!ReadyEntries(manifest).Any(e => Text(e["Target"]!.AsObject(),"Id") == Text(vote,"CheckInId"))) continue;
            await using var insert = InsertCommand("CheckInHelpfulVotes",vote,db["Shops"],tx);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public static Dictionary<string,int> Counts(JsonObject manifest, IEnumerable<JsonObject> canonical)
    {
        var set = canonical.Select(c => (Id: Text(c,"Id")!,User: Text(c,"UserId")!)).ToHashSet();
        var merged = manifest["Mappings"]!.AsArray().OfType<JsonObject>().Where(e => Text(e,"SourceTable") == "CheckIns" && Text(e,"Outcome") == "merged").Select(e => Text(e,"SourceId")).ToHashSet();
        foreach (var old in Rows(manifest["Inventory"]!.AsObject(),"CheckIns").Where(v => !merged.Contains(Text(v,"Id"))))
            set.Add((Text(old,"Id")!,Text(old,"UserId")!));
        return set.GroupBy(x => x.User).ToDictionary(g => g.Key,g => g.Count());
    }

    public static async Task ReconcileCountsAsync(JsonObject manifest, IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        var canonical = (await ReadAsync(db["Shops"],"CanonicalCheckIns","",ct)).OfType<JsonObject>();
        var counts = Counts(manifest,canonical);
        await using var tx = await db["Account"].BeginTransactionAsync(ct);
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(7402100401); LOCK TABLE \"Users\" IN SHARE ROW EXCLUSIVE MODE",db["Account"],tx))
            await guard.ExecuteNonQueryAsync(ct);
        foreach (var user in Rows(manifest["Inventory"]!.AsObject(),"Users"))
        {
            var id = Text(user,"Id")!;
            await using var command = new NpgsqlCommand("UPDATE \"Users\" SET \"CheckInCount\"=@count WHERE \"Id\"=@id",db["Account"],tx);
            command.Parameters.AddWithValue("count",counts.GetValueOrDefault(id));
            command.Parameters.AddWithValue("id",Guid.Parse(id));
            await command.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        manifest["CountsCompleted"] = true;
    }

    public static async Task InvalidateAsync(JsonObject manifest, string redisConnection, CancellationToken ct)
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
        // Use the strict existing invalidator: success must not swallow a Redis outage.
        IStrictCacheInvalidator cache = new RedisService(redis,NullLogger<RedisService>.Instance);
        foreach (var entry in ReadyEntries(manifest))
        {
            var row = entry["Target"]!.AsObject();
            var shop = Guid.Parse(Text(row,"ShopId")!); var user = Guid.Parse(Text(row,"UserId")!);
            foreach (var key in new[] { CacheKey.Shop.Detail(shop),CacheKey.Shop.Visited(user),CacheKey.Shop.VisitStatistics(user),
                CacheKey.User.Profile(user),CacheKey.User.Entity(user) }) await cache.RemoveAsync(key,ct);
        }
        await cache.RemoveByPatternAsync(CacheKey.Shop.SearchPattern(),ct);
        await cache.RemoveByPatternAsync(CacheKey.Shop.ListPattern(),ct);
        await cache.RemoveAsync(CacheKey.Shop.PublicPlatformStats(),ct);
        manifest["CacheInvalidationPending"] = false;
    }

    public static async Task<JsonObject> ReconcileAsync(JsonObject manifest, IReadOnlyDictionary<string,NpgsqlConnection> db, CancellationToken ct)
    {
        var failures = new JsonArray();
        var inventory = manifest["Inventory"]!.AsObject();
        foreach (var (service,tables) in SourceTables)
        foreach (var table in tables.Where(t => t is "Reviews" or "CheckIns" or "ReviewHelpfulVotes" or "ReviewReports" or "ModerationReviews"))
        {
            var current = await ReadAsync(db[service],table,"",ct);
            if (Hash(current) != Hash(inventory[table]!)) failures.Add($"Archive changed: {service}.{table}");
        }
        foreach (var entry in manifest["Mappings"]!.AsArray().OfType<JsonObject>())
        {
            if (Text(entry,"Outcome") is "unresolved" or "conflict") { failures.Add($"{Text(entry,"SourceTable")}/{Text(entry,"SourceId")}: {Text(entry,"Reason")}"); continue; }
            if (entry["Target"] is not JsonObject expected) continue;
            var current = await FindAsync(db["Shops"],"CanonicalCheckIns",Text(expected,"Id")!,ct);
            if (current == null || !Matches(expected,current)) failures.Add("Target mismatch: "+Text(expected,"Id"));
            foreach (var snap in entry["Snapshots"]!.AsArray().OfType<JsonObject>())
            {
                var actual = await FindAsync(db["Moderation"],"ModerationCheckIns",Text(snap,"Id")!,ct);
                if (actual == null || !Matches(snap,actual)) failures.Add("Snapshot mismatch: "+Text(snap,"Id"));
            }
        }
        foreach (var native in Rows(inventory,"CanonicalCheckIns"))
        {
            var actual = await FindAsync(db["Shops"],"CanonicalCheckIns",Text(native,"Id")!,ct);
            if (actual == null || !Matches(native,actual)) failures.Add("Native canonical changed: "+Text(native,"Id"));
        }
        foreach (var native in Rows(inventory,"ModerationCheckIns"))
        {
            var actual = await FindAsync(db["Moderation"],"ModerationCheckIns",Text(native,"Id")!,ct);
            if (actual == null || !Matches(native,actual)) failures.Add("Native snapshot changed: "+Text(native,"Id"));
        }
        var copiedVotes = await ReadAsync(db["Shops"],"CheckInHelpfulVotes","",ct);
        foreach (var vote in manifest["Votes"]!.AsArray().OfType<JsonObject>())
            if (!copiedVotes.OfType<JsonObject>().Any(v => Matches(vote,v))) failures.Add("Vote mismatch: "+Text(vote,"CheckInId")+"/"+Text(vote,"UserId"));
        foreach (var outcome in manifest["VoteOutcomes"]!.AsArray().OfType<JsonObject>().Where(o => Text(o,"Outcome") == "unresolved"))
            failures.Add("Unresolved vote: "+Text(outcome,"SourceId"));
        foreach (var outcome in manifest["ReportOutcomes"]!.AsArray().OfType<JsonObject>().Where(o => Text(o,"Outcome") == "unresolved"))
            failures.Add("Unresolved report target: "+Text(outcome,"SourceId"));
        await ReconcilePhotosAsync(manifest,db,failures,ct);
        var allCanonical = (await ReadAsync(db["Shops"],"CanonicalCheckIns","",ct)).OfType<JsonObject>().ToArray();
        var counts = Counts(manifest,allCanonical);
        var users = await ReadAsync(db["Account"],"Users","",ct);
        foreach (var original in Rows(inventory,"Users"))
        {
            var actual = users.OfType<JsonObject>().SingleOrDefault(u => Text(u,"Id") == Text(original,"Id"));
            var preserved = (JsonObject)original.DeepClone(); preserved.Remove("CheckInCount");
            if (actual == null || !Matches(preserved,actual)) failures.Add("Other account fields changed: "+Text(original,"Id"));
            if (actual == null || Int(actual,"CheckInCount") != counts.GetValueOrDefault(Text(original,"Id")!)) failures.Add("Account count mismatch: "+Text(original,"Id"));
        }
        if (Flag(manifest,"CacheInvalidationPending")) failures.Add("Durable cache invalidation pending");
        return new JsonObject {
            ["SourceReviews"] = Rows(inventory,"Reviews").Count(), ["SourceVisits"] = Rows(inventory,"CheckIns").Count(),
            ["SourceModeration"] = Rows(inventory,"ModerationReviews").Count(), ["CanonicalTargets"] = ReadyEntries(manifest).Count(),
            ["NativeCanonical"] = Rows(inventory,"CanonicalCheckIns").Count(), ["Votes"] = manifest["Votes"]!.AsArray().Count,
            ["Reports"] = Rows(inventory,"ReviewReports").Count(), ["ReportHash"] = Hash(inventory["ReviewReports"]!),
            ["Outcomes"] = new JsonObject(manifest["Mappings"]!.AsArray().OfType<JsonObject>().GroupBy(e => Text(e,"Outcome")!)
                .Select(g => KeyValuePair.Create<string,JsonNode?>(g.Key,JsonValue.Create(g.Count())))),
            ["Failures"] = failures, ["ReleaseReady"] = failures.Count == 0
        };
    }
    public static bool EnvelopeBlocksCountReconciliation(string table,JsonObject envelope) =>
        !table.Contains("incoming_envelopes",StringComparison.Ordinal) || Text(envelope,"status") != "Handled";

    public static async Task AssertDrainedAsync(IReadOnlyDictionary<string,NpgsqlConnection> db,CancellationToken ct)
    {
        foreach (var (service,connection) in db)
        {
            var names = new List<(string Schema,string Table)>();
            await using (var command = new NpgsqlCommand("""
                SELECT table_schema,table_name FROM information_schema.tables
                WHERE (table_name LIKE 'wolverine%envelopes%' OR table_name='wolverine_dead_letters') AND table_type='BASE TABLE'
                ORDER BY table_schema,table_name
                """,connection))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) names.Add((reader.GetString(0),reader.GetString(1)));
            foreach (var (schema,table) in names)
            {
                var builder = new NpgsqlCommandBuilder();
                var quoted = builder.QuoteIdentifier(schema)+"."+builder.QuoteIdentifier(table);
                var where = table.Contains("incoming_envelopes",StringComparison.Ordinal)
                    ? " WHERE (to_jsonb(t)->>'status') IS DISTINCT FROM 'Handled'" : "";
                await using var command = new NpgsqlCommand($"SELECT EXISTS(SELECT 1 FROM {quoted} t{where})",connection);
                if ((bool)(await command.ExecuteScalarAsync(ct))!)
                    throw new InvalidOperationException("Unprocessed/scheduled/outgoing/dead-letter envelopes remain: "+service+"."+table);
            }
        }
    }

}