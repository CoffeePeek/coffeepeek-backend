using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using Npgsql;
using NpgsqlTypes;

namespace CoffeePeek.LegacyCheckInsMigration;

public static partial class LegacyReviewBackfill
{
    public static void PreparePhotos(JsonObject manifest)
    {
        var inventory = manifest["Inventory"]!.AsObject();
        var bindings = new JsonArray();
        var outcomes = new JsonArray();
        var membership = new Dictionary<string,List<(JsonObject Entry, JsonObject? Snapshot, bool Current)>>();
        void Add(string service, JsonObject photo, JsonObject entry, JsonObject? snapshot, bool current)
        {
            var reference = service+"/"+Text(photo,"Id");
            if (!membership.TryGetValue(reference,out var list)) membership[reference] = list = [];
            list.Add((entry,snapshot,current));
        }
        foreach (var entry in manifest["Mappings"]!.AsArray().OfType<JsonObject>().Where(e => e["Target"] != null))
        {
            var target = entry["Target"]!.AsObject();
            var snapshots = entry["Snapshots"]!.AsArray().OfType<JsonObject>().ToArray();
            var reviewId = Text(entry,"SourceTable") == "Reviews" ? Text(entry,"SourceId") : null;
            var modId = Text(entry,"ModerationSourceId");
            var visitId = Text(entry,"VisitSourceId");
            foreach (var snapshot in snapshots)
            {
                var current = Int(snapshot,"ContentRevision") == Int(target,"ContentRevision");
                var useModeration = current && modId != null;
                foreach (var photo in Rows(inventory,useModeration ? "ModerationPhotos" : "ShopPhotos")
                    .Where(p => Text(p,useModeration ? "ModerationReviewId" : "ReviewId") == (useModeration ? modId : reviewId)
                        && (useModeration ? modId : reviewId) != null))
                    Add(useModeration ? "Moderation" : "Shops",photo,entry,snapshot,current);
            }
            // Linked visit archives are explicit source membership, including photos omitted by old review events.
            if (visitId != null)
                foreach (var photo in Rows(inventory,"ShopPhotos").Where(p => Text(p,"CheckInId") == visitId))
                    Add("Shops",photo,entry,modId == null ? snapshots.LastOrDefault() : snapshots.FirstOrDefault(),modId == null || snapshots.Length == 1);
            // Equal published/current review content shares a snapshot but both evidenced sets remain in audit.
            if (reviewId != null && snapshots.Length == 1 && modId != null)
                foreach (var photo in Rows(inventory,"ShopPhotos").Where(p => Text(p,"ReviewId") == reviewId))
                    Add("Shops",photo,entry,snapshots[0],true);
        }
        foreach (var service in new[]{"Shops","Moderation"})
        foreach (var photo in Rows(inventory,service == "Shops" ? "ShopPhotos" : "ModerationPhotos"))
        {
            var reference = service+"/"+Text(photo,"Id");
            if (!membership.TryGetValue(reference,out var parents))
            {
                var legacyParent = Text(photo,service == "Shops" ? "ReviewId" : "ModerationReviewId") ?? Text(photo,"CheckInId");
                outcomes.Add(Outcome(service+".ShopPhotos",Text(photo,"Id")!,legacyParent == null ? "retained" : "unresolved",
                    legacyParent == null ? "unrelated gallery" : "legacy parent not mapped"));
                continue;
            }
            var entry = parents[0].Entry; var target = entry["Target"]!.AsObject();
            var key = Text(photo,"StorageKey"); var owner = Text(target,"UserId"); var visit = Text(target,"Id")!;
            var media = Rows(inventory,"Photos").Where(p => key != null && (Text(p,"StorageKey") == key || Text(p,"OriginalStorageKey") == key)).ToArray();
            string? error = null;
            if (string.IsNullOrWhiteSpace(key) || key.Length > 255 || key.IndexOfAny(['*','?']) >= 0) error = "unsafe/missing key";
            else if (parents.Any(p => Text(p.Entry["Target"]!.AsObject(),"Id") != visit)) error = "source parent collision";
            else if (Text(photo,"OwnerId") is { } po && po != owner) error = "archive owner conflict";
            else if (Text(photo,service == "Shops" ? "CoffeeShopId" : "ModerationShopId") != null) error = "archive photo shared with unrelated parent";
            else if (media.Length != 1) error = "missing/ambiguous exact-key Media metadata";
            else if (Text(media[0],"OwnerId") != owner || Int(media[0],"OwnerType") != 1) error = "Media owner conflict";
            else if (Text(media[0],"CheckInId") is { } parent && parent != visit) error = "Media claimed by another visit";
            else if (Int(media[0],"BucketType") is not (1 or 2) || Int(media[0],"Status") is not (0 or 1) || media[0]["DeletedAt"] != null || media[0]["ScheduledDeletionAt"] != null) error = "unsupported/deleted Media object";
            else if (Text(photo,"ContentType") != Text(media[0],"ContentType") || Text(photo,"SizeBytes") != Text(media[0],"SizeBytes")
                || string.IsNullOrWhiteSpace(Text(media[0],"ContentType")) || string.IsNullOrWhiteSpace(Text(media[0],"FileName"))) error = "source/Media size or MIME conflict";
            else if (Int(photo,"SortIndex") < 0) error = "invalid gallery order";
            else if (Rows(inventory,"Gallery").Any(g => Text(g["Row"]!.AsObject(),"StorageKey") == key
                && (Text(g,"Table") != "ShopPhotos" || !membership.TryGetValue(Text(g,"Service")+"/"+Text(g["Row"]!.AsObject(),"Id"),out var owners)))) error = "key shared with unrelated gallery";
            else if (Rows(inventory,"Gallery").Any(g => Text(g["Row"]!.AsObject(),"StorageKey") == key
                && membership.TryGetValue(Text(g,"Service")+"/"+Text(g["Row"]!.AsObject(),"Id"),out var owners)
                && owners.Any(p => Text(p.Entry["Target"]!.AsObject(),"Id") != visit))) error = "key shared with another visit";
            if (error == null && Rows(inventory,"ShopPhotos").Any(p => Text(p,"Id") == Text(media[0],"Id")))
                error = "Media UUID collides with retained Shops photo UUID";
            if (error != null)
            {
                outcomes.Add(Outcome(service+".ShopPhotos",Text(photo,"Id")!,"unresolved",error));
                foreach (var p in parents) { p.Entry["PhotosUnresolved"] = true; p.Entry["PhotoPlanningUnresolved"] = true; }
                continue;
            }
            var binding = bindings.OfType<JsonObject>().SingleOrDefault(b => Text(b,"MediaId") == Text(media[0],"Id"));
            if (binding == null)
            {
                binding = new JsonObject { ["MediaId"] = Text(media[0],"Id"), ["CheckInId"] = visit, ["OwnerId"] = owner,
                    ["OriginalKey"] = key, ["SourceKey"] = Text(media[0],"StorageKey"), ["SourceBucket"] = Int(media[0],"BucketType"),
                    ["MediaOriginal"] = media[0].DeepClone(), ["SourceReferences"] = new JsonArray(), ["Snapshots"] = new JsonArray(),
                    ["Current"] = false, ["SortIndex"] = Int(photo,"SortIndex") };
                bindings.Add(binding);
            }
            if (Text(binding,"CheckInId") != visit) throw new InvalidOperationException("Photo canonical parent collision.");
            binding["SourceReferences"]!.AsArray().Add(reference);
            if (parents.Any(p => p.Current)) { binding["Current"] = true; binding["SortIndex"] = Int(photo,"SortIndex"); }
            foreach (var p in parents.Where(p => p.Snapshot != null))
            {
                var snapshotId = Text(p.Snapshot!,"Id")!;
                if (!binding["Snapshots"]!.AsArray().OfType<JsonObject>().Any(s => Text(s,"SnapshotId") == snapshotId))
                    binding["Snapshots"]!.AsArray().Add(new JsonObject { ["SnapshotId"] = snapshotId, ["SortIndex"] = Int(photo,"SortIndex") });
            }
            var mapped = Outcome(service+".ShopPhotos",Text(photo,"Id")!,"mapped",null); mapped["SourceHash"] = Hash(photo); outcomes.Add(mapped);
        }
        manifest["Photos"] = bindings; manifest["PhotoOutcomes"] = outcomes;
    }

    public static async Task ApplyPhotosAsync(JsonObject manifest, IReadOnlyDictionary<string,NpgsqlConnection> db,
        IStorageService storage, string manifestPath, CancellationToken ct)
    {
        var photos = manifest["Photos"]!.AsArray().OfType<JsonObject>().OrderBy(b => Text(b,"MediaId")).ToArray();
        // Same ordered session advisory keys as runtime claims; retain leases through byte work + metadata commits.
        foreach (var key in photos.Select(p => BinaryPrimitives.ReadInt64LittleEndian(Guid.Parse(Text(p,"MediaId")!).ToByteArray())).Distinct().Order())
        {
            await using var claim = new NpgsqlCommand("SELECT pg_advisory_lock(@key)",db["Media"]);
            claim.Parameters.AddWithValue("key",key); await claim.ExecuteNonQueryAsync(ct);
        }
        foreach (var binding in photos)
        {
            var entry = ReadyEntries(manifest).SingleOrDefault(e => Text(e["Target"]!.AsObject(),"Id") == Text(binding,"CheckInId"));
            if (entry == null) continue;
            var original = binding["MediaOriginal"]!.AsObject();
            var current = await FindAsync(db["Media"],"Photos",Text(binding,"MediaId")!,ct)
                ?? throw new InvalidOperationException("Historical Media row disappeared.");
            var planned = (JsonObject)original.DeepClone();
            planned["CheckInId"] = Text(binding,"CheckInId"); planned["OriginalStorageKey"] = Text(binding,"OriginalKey");
            planned["StorageKey"] = "check-ins/"+Guid.Parse(Text(binding,"MediaId")!).ToString("N")+Path.GetExtension(Text(binding,"SourceKey"));
            planned["BucketType"] = 2; planned["Status"] = 1;
            // Deterministic audit time survives retries and is not invented historical moderation evidence.
            planned["ClaimedAtUtc"] = manifest["StartedAtUtc"]!.DeepClone();
            if (!Matches(original,current) && !Matches(planned,current)) throw new InvalidOperationException("Media metadata changed/owner conflict.");
            try
            {
                var source = Text(binding,"SourceKey")!;
                var bucket = (BucketType)Int(binding,"SourceBucket");
                var expectedSize = long.Parse(Text(original,"SizeBytes")!);
                if (await storage.GetObjectSize(source,bucket,ct) != expectedSize) throw new InvalidOperationException("Historical source bytes/size unavailable.");
                var destination = await storage.CopyHistoricalToCheckInStorageAsync(source,Guid.Parse(Text(binding,"MediaId")!),
                    Guid.Parse(Text(binding,"CheckInId")!),ct,bucket);
                if (destination != Text(planned,"StorageKey") || await storage.GetObjectSize(destination,BucketType.CheckIn,ct) != expectedSize)
                    throw new InvalidOperationException("Private byte destination mismatch.");
                var hash = await ReadByteHashAsync(storage,destination,BucketType.CheckIn,ct);
                if (Text(binding,"ByteHash") is { } prior && prior != hash) throw new InvalidOperationException("Copied bytes changed since prior attempt.");
                binding["ByteHash"] = hash; binding["ByteSize"] = expectedSize; binding["MediaTarget"] = planned.DeepClone();
                Save(manifestPath,manifest); // Before metadata writes: a failed save never publishes a locator.
                await using var tx = await db["Media"].BeginTransactionAsync(ct);
                await using var update = new NpgsqlCommand("""
                    UPDATE "Photos" p SET "CheckInId"=r."CheckInId","OriginalStorageKey"=r."OriginalStorageKey",
                        "StorageKey"=r."StorageKey","BucketType"=r."BucketType","Status"=r."Status","ClaimedAtUtc"=r."ClaimedAtUtc"
                    FROM jsonb_populate_record(NULL::"Photos",@row) r
                    WHERE p."Id"=r."Id" AND p."OwnerId"=r."OwnerId" AND (p."CheckInId" IS NULL OR p."CheckInId"=r."CheckInId")
                    """,db["Media"],tx);
                update.Parameters.Add("row",NpgsqlDbType.Jsonb).Value = planned.ToJsonString();
                if (await update.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Historical Media claim changed.");
                await tx.CommitAsync(ct);
                var shopRow = new JsonObject { ["Id"] = Text(binding,"MediaId"), ["FileName"] = Text(original,"FileName"),
                    ["ContentType"] = Text(original,"ContentType"), ["StorageKey"] = destination, ["SizeBytes"] = expectedSize,
                    ["OwnerId"] = Text(binding,"OwnerId"), ["SortIndex"] = Int(binding,"SortIndex"),
                    ["CanonicalCheckInId"] = Text(binding,"CheckInId"), ["CreatedAtUtc"] = original["UploadedAt"]!.DeepClone(), ["UpdatedAtUtc"] = null };
                if (Flag(binding,"Current")) await InsertPhotoAsync(db["Shops"],"CheckInPhotos",shopRow,ct);
                binding["ShopTarget"] = Flag(binding,"Current") ? shopRow.DeepClone() : null;
                var snapshotRows = new JsonArray();
                foreach (var snapshot in binding["Snapshots"]!.AsArray().OfType<JsonObject>())
                {
                    var row = (JsonObject)shopRow.DeepClone(); row.Remove("CanonicalCheckInId");
                    row["Id"] = StableId(Text(snapshot,"SnapshotId")+"/"+Text(binding,"MediaId")).ToString();
                    row["ModerationCheckInId"] = Text(snapshot,"SnapshotId"); row["CheckInPhotoId"] = Text(binding,"MediaId");
                    row["SortIndex"] = Int(snapshot,"SortIndex");
                    await InsertPhotoAsync(db["Moderation"],"ShopPhotos",row,ct); snapshotRows.Add(row);
                }
                binding["SnapshotTargets"] = snapshotRows; binding["Completed"] = true; Save(manifestPath,manifest);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                entry["PhotosUnresolved"] = true; binding["Failure"] = ex is InvalidOperationException ? ex.Message : ex.GetType().Name + ": storage/provider failure";
                Save(manifestPath,manifest);
                // Fail closed: later visits can still be audited; this visit remains staged private.
            }
        }
    }
    private static async Task InsertPhotoAsync(NpgsqlConnection connection,string table,JsonObject row,CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        var actual = await FindAsync(connection,table,Text(row,"Id")!,ct,tx);
        if (actual != null && !Matches(row,actual)) throw new InvalidOperationException("Retained/native photo UUID collision.");
        if (actual == null)
        {
            await using var insert = InsertCommand(table,row,connection,tx);
            if (await insert.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Photo insertion collision.");
        }
        await tx.CommitAsync(ct);
    }
    private static async Task ReconcilePhotosAsync(JsonObject manifest,IReadOnlyDictionary<string,NpgsqlConnection> db,JsonArray failures,CancellationToken ct)
    {
        foreach (var outcome in manifest["PhotoOutcomes"]!.AsArray().OfType<JsonObject>().Where(o => Text(o,"Outcome") == "unresolved"))
            failures.Add("Unresolved photo: "+Text(outcome,"SourceId")+" "+Text(outcome,"Reason"));
        foreach (var binding in manifest["Photos"]!.AsArray().OfType<JsonObject>())
        {
            if (!Flag(binding,"Completed")) { failures.Add("Unverified bytes: "+Text(binding,"MediaId")); continue; }
            var actual = await FindAsync(db["Media"],"Photos",Text(binding,"MediaId")!,ct);
            if (actual == null || !Matches(binding["MediaTarget"]!.AsObject(),actual)) failures.Add("Media binding mismatch: "+Text(binding,"MediaId"));
            if (binding["ShopTarget"] is JsonObject shop)
            {
                var row = await FindAsync(db["Shops"],"CheckInPhotos",Text(shop,"Id")!,ct);
                if (row == null || !Matches(shop,row)) failures.Add("Shop photo mismatch: "+Text(shop,"Id"));
            }
            foreach (var snapshot in binding["SnapshotTargets"]!.AsArray().OfType<JsonObject>())
            {
                var row = await FindAsync(db["Moderation"],"ShopPhotos",Text(snapshot,"Id")!,ct);
                if (row == null || !Matches(snapshot,row)) failures.Add("Snapshot photo mismatch: "+Text(snapshot,"Id"));
            }
        }
    }
    public static async Task AssertArchivesAsync(JsonObject manifest,IReadOnlyDictionary<string,NpgsqlConnection> db,CancellationToken ct)
    {
        var inventory = manifest["Inventory"]!.AsObject();
        foreach (var service in new[]{"Shops","Moderation"})
        foreach (var table in SourceTables[service].Where(t => t is "Reviews" or "CheckIns" or "ReviewHelpfulVotes" or "ReviewReports" or "ModerationReviews" or "ShopPhotos"))
        {
            var key = service == "Moderation" && table == "ShopPhotos" ? "ModerationPhotos" : table;
            var condition = table == "ShopPhotos" ? (service == "Shops" ? " WHERE \"CanonicalCheckInId\" IS NULL" : " WHERE \"ModerationCheckInId\" IS NULL") : "";
            if (Hash(await ReadAsync(db[service],table,condition,ct)) != Hash(inventory[key]!))
                throw new InvalidOperationException("Source inventory drift: "+service+"."+table);
        }
    }

    public static async Task VerifyPhotoBytesAsync(JsonObject manifest,IStorageService storage,JsonArray failures,CancellationToken ct)
    {
        foreach (var binding in manifest["Photos"]!.AsArray().OfType<JsonObject>().Where(b => Flag(b,"Completed")))
        {
            try
            {
                foreach (var (key,bucket) in new[]{(Text(binding,"SourceKey")!, (BucketType)Int(binding,"SourceBucket")),
                    (Text(binding["MediaTarget"]!.AsObject(),"StorageKey")!,BucketType.CheckIn)})
                {
                    if (await storage.GetObjectSize(key,bucket,ct) != long.Parse(Text(binding,"ByteSize")!)
                        || await ReadByteHashAsync(storage,key,bucket,ct) != Text(binding,"ByteHash"))
                        failures.Add("Photo bytes mismatch: "+Text(binding,"MediaId"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { failures.Add("Photo byte verification unavailable: "+Text(binding,"MediaId")+" "+ex.GetType().Name); }
        }
    }

    private static async Task<string> ReadByteHashAsync(IStorageService storage,string key,BucketType bucket,CancellationToken ct)
    {
        using var hash = SHA256.Create();
        await using var sink = new CryptoStream(Stream.Null,hash,CryptoStreamMode.Write);
        await storage.CopyToAsync(key,bucket,sink,ct);
        await sink.FlushFinalBlockAsync(ct);
        return Convert.ToHexString(hash.Hash!);
    }

}
