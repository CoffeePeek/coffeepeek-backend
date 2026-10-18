using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Services;

public interface IStorageService
{
    Task Put(string storageKey, string contentType, Stream stream, long sizeBytes, BucketType bucketType, CancellationToken ct = default);
    Task<PresignedPhotoMetaData> GetPresignedUploadUrl(
        string fileName,
        string contentType,
        BucketType bucketType,
        CancellationToken ct = default,
        string? keyPrefix = null);

    Task MarkAsPermanent(string storageKey, BucketType bucketType, CancellationToken ct = default);
    Task<bool> Exists(string uploadedPhotoStorageKey, BucketType bucketType, CancellationToken ct = default);

    /// <summary>Size in bytes of the stored object, or null when the object does not exist.</summary>
    Task<long?> GetObjectSize(string storageKey, BucketType bucketType, CancellationToken ct = default);
    Task Delete(string oldStorageKey, BucketType bucketType, CancellationToken ct = default);
    Task EnsureCheckInBucketPrivateAsync(CancellationToken ct = default);
    Task<bool> IsCheckInBucketPrivateAsync(CancellationToken ct = default);
    Task<string> MoveToCheckInStorageAsync(string key, Guid photoId, Guid checkInId, CancellationToken ct = default,
        BucketType sourceBucket = BucketType.Shop);
    /// <summary>Verify a private historical copy and deny the old public locator while retaining source bytes.</summary>
    Task<string> CopyHistoricalToCheckInStorageAsync(string key, Guid photoId, Guid checkInId, CancellationToken ct = default,
        BucketType sourceBucket = BucketType.Shop);
    Task CopyToAsync(string key, BucketType bucket, Stream destination, CancellationToken ct = default);
}
