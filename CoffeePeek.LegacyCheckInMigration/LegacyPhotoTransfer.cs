using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;

namespace CoffeePeek.LegacyCheckInMigration;

public static class LegacyPhotoTransfer
{
    public static async Task RunAsync(MigrationDatabase shops, MigrationDatabase moderation, MigrationDatabase media,
        IStorageService? storage, IReadOnlyList<LegacyCheckIn> plans)
    {
        var shopPhotos = await shops.RowsAsync("ShopPhotos");
        var moderationPhotos = await moderation.RowsAsync("ShopPhotos");
        var metadata = await media.RowsAsync("Photos");
        // Keys used by current entities must remain in their existing buckets.
        var retainedKeys = shopPhotos.Where(x => x.Id("CoffeeShopId") != Guid.Empty || x.Id("CanonicalCheckInId") != Guid.Empty)
            .Select(x => x.Text("StorageKey")!).ToHashSet();
        foreach (var table in new[] { "CheckInPhotos", "RoasterPhotos", "ShopMenuPhotos", "CoffeeProductPhotos" })
            foreach (var photo in await shops.RowsAsync(table)) retainedKeys.Add(photo.Text("StorageKey")!);
        foreach (var photo in moderationPhotos.Where(x => x.Id("ModerationShopId") != Guid.Empty || x.Id("ModerationCheckInId") != Guid.Empty))
            retainedKeys.Add(photo.Text("StorageKey")!);
        foreach (var photo in await moderation.RowsAsync("ModerationRoasterPhotos")) retainedKeys.Add(photo.Text("StorageKey")!);
        var legacyPhotos = shopPhotos.Where(x => x.Id("CheckInId") != Guid.Empty || x.Id("ReviewId") != Guid.Empty)
            .Concat(moderationPhotos.Where(x => x.Id("ModerationReviewId") != Guid.Empty)).ToList();
        if (legacyPhotos.Count == 0) return;
        ArgumentNullException.ThrowIfNull(storage);
        await storage.EnsureCheckInBucketPrivateAsync();
        foreach (var plan in plans)
        {
            var candidates = legacyPhotos.Where(x =>
                    (plan.VisitId != null && x.Id("CheckInId") == plan.VisitId) ||
                    (plan.ReviewId != null && x.Id("ReviewId") == plan.ReviewId) ||
                    (plan.ModerationId != null && x.Id("ModerationReviewId") == plan.ModerationId))
                .OrderBy(x => x.Number("SortIndex") ?? 0).ThenBy(x => x.Text("CreatedAtUtc")).ThenBy(x => x.Id())
                .GroupBy(x => x.Text("StorageKey")).Select(x => x.First()).ToList();
            var migratedKeys = new HashSet<string>();
            var sortIndex = 0;
            foreach (var photo in candidates)
            {
                if (sortIndex == 5) break;
                var key = photo.Text("StorageKey")!;
                var owner = plan.CheckIn.Id("UserId");
                var photoId = LegacyRow.StableId($"photo:{plan.CheckIn.Id()}:{key}");
                var existing = metadata.FirstOrDefault(x => x.Id() == photoId && x.Id("CheckInId") == plan.CheckIn.Id() && x.Id("OwnerId") == owner);
                var source = metadata.FirstOrDefault(x => x.Text("StorageKey") == key && x.Id("OwnerId") == owner &&
                    x.Text("OwnerType") is "User" or "Shop" && x.Text("BucketType") == "Shop" && x.Text("Status") == "Confirmed");
                source ??= existing;
                // Legacy moderation photos can have a null OwnerId; Media ownership is authoritative.
                if (source == null || (photo.Id("OwnerId") != Guid.Empty && photo.Id("OwnerId") != owner)) continue;
                var target = existing?.Text("StorageKey") ?? $"check-ins/{photoId:N}{Path.GetExtension(key)}";
                // Only a committed claim proves an earlier private copy was verified.
                // An ambiguous PUT may have left truncated bytes; copy again from the legacy source.
                var readKey = existing != null && await storage.Exists(target, BucketType.CheckIn) ? target : key;
                var readBucket = readKey == target ? BucketType.CheckIn : BucketType.Shop;
                var size = await storage.GetObjectSize(readKey, readBucket);
                if (size is null or <= 0) continue;
                await using var data = OpenScratchFile();
                await storage.CopyToAsync(readKey, readBucket, data);
                data.Position = 0;
                var header = new byte[16];
                var headerSize = await data.ReadAsync(header);
                var format = CoffeePhotoFormat.Detect(header.AsSpan(0, headerSize));
                if (format == null && headerSize >= 2 && header.AsSpan(0, 2).SequenceEqual("BM"u8)) format = ("image/bmp", "bmp");
                if (format == null || data.Length != size) continue;
                if (readBucket != BucketType.CheckIn)
                {
                    data.Position = 0;
                    await storage.Put(target, format.Value.ContentType, data, data.Length, BucketType.CheckIn);
                    await using var verification = OpenScratchFile();
                    await storage.CopyToAsync(target, BucketType.CheckIn, verification);
                    data.Position = 0;
                    verification.Position = 0;
                    var sourceHash = await SHA256.HashDataAsync(data);
                    var targetHash = await SHA256.HashDataAsync(verification);
                    if (verification.Length != data.Length || !CryptographicOperations.FixedTimeEquals(
                        sourceHash, targetHash))
                        throw new InvalidOperationException("Private legacy photo copy could not be verified.");
                }
                await storage.MarkAsPermanent(target, BucketType.CheckIn);
                var newMetadata = source.Pick("FileName", "UploadedAt", "OwnerType", "OwnerId");
                newMetadata["Id"] = photoId.ToString();
                newMetadata["ContentType"] = format.Value.ContentType;
                newMetadata["StorageKey"] = target;
                newMetadata["SizeBytes"] = size.Value;
                newMetadata["Status"] = "Confirmed";
                newMetadata["BucketType"] = "CheckIn";
                newMetadata["CheckInId"] = plan.CheckIn.Id().ToString();
                newMetadata["ClaimedAtUtc"] = DateTime.UtcNow;
                newMetadata["OriginalStorageKey"] = key;
                await media.InsertAsync("Photos", newMetadata);
                var newPhoto = newMetadata.Pick("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId");
                newPhoto["CanonicalCheckInId"] = plan.CheckIn.Id().ToString();
                newPhoto["SortIndex"] = sortIndex++;
                newPhoto["CreatedAtUtc"] = photo["CreatedAtUtc"]?.DeepClone() ?? source["UploadedAt"]!.DeepClone();
                await shops.InsertAsync("CheckInPhotos", newPhoto);
                migratedKeys.Add(key);
                if (plan.Snapshot != null)
                {
                    var newSnapshotPhoto = newPhoto.Pick("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId", "SortIndex", "CreatedAtUtc");
                    newSnapshotPhoto["Id"] = LegacyRow.StableId($"snapshot-photo:{plan.ModerationId}:{photoId}").ToString();
                    newSnapshotPhoto["CheckInPhotoId"] = photoId.ToString();
                    newSnapshotPhoto["ModerationShopId"] = null;
                    newSnapshotPhoto["ModerationReviewId"] = null;
                    // FK is attached after the snapshot itself is inserted.
                    await moderation.InsertAsync("ShopPhotos", newSnapshotPhoto);
                }
            }
            // A decision about different photos cannot authorize the reconstructed revision.
            var approvedKeys = moderationPhotos.Where(x => plan.ModerationId != null && x.Id("ModerationReviewId") == plan.ModerationId)
                .Select(x => x.Text("StorageKey")!).ToHashSet();
            if (plan.Snapshot != null && !approvedKeys.SetEquals(migratedKeys))
            {
                plan.CheckIn["Visibility"] = 0;
                plan.CheckIn["ModerationState"] = 0;
                plan.CheckIn["ApprovedRevision"] = null;
                plan.CheckIn["ModerationSubmissionId"] = null;
                plan.CheckIn["ModerationDecisionSequence"] = 0;
                plan.CheckIn["FirstPublishedAtUtc"] = null;
                // Suppress an obsolete snapshot so a new submission of revision 1 is possible.
                plan.Snapshot["IsSoftDelete"] = true;
                await shops.ExecuteAsync("""
                    UPDATE "CanonicalCheckIns" SET "Visibility"=0, "ModerationState"=0, "ApprovedRevision"=NULL,
                    "ModerationSubmissionId"=NULL, "ModerationDecisionSequence"=0, "FirstPublishedAtUtc"=NULL WHERE "Id"=@id
                    """, ("id", plan.CheckIn.Id()));
            }
        }
        // Delete only source objects actually attached to obsolete records, preserving gallery/catalog assets.
        foreach (var key in legacyPhotos.Select(x => x.Text("StorageKey")!).Distinct().Where(x => !retainedKeys.Contains(x)))
        {
            if (storage is MinIOStorageService minio) await minio.DeleteHistoricalCheckInObjectAsync(key);
            else await storage.Delete(key, BucketType.Shop);
            if (await storage.Exists(key, BucketType.Shop)) throw new InvalidOperationException("Legacy source photo deletion could not be verified.");
            await media.ExecuteAsync("DELETE FROM \"Photos\" WHERE \"StorageKey\"=@key AND \"BucketType\"='Shop' AND \"CheckInId\" IS NULL", ("key", key));
        }
    }

    private static FileStream OpenScratchFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete,
            65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        // Unlink immediately: only the open handle remains, including if the process is killed.
        // This is bounded-memory byte processing, never a backup or persistent export.
        try { File.Delete(path); return stream; }
        catch { stream.Dispose(); throw; }
    }
}
