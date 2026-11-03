using System.Net;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

/// <summary>
/// Reserves one collection cover upload for one Shops collection. The upload must belong to the caller, be fully uploaded,
/// unused and a JPEG/PNG/WebP raster of at most 1 MiB and 1280 px per side. Retrying the same collection is idempotent.
/// </summary>
public class ClaimCollectionCoverHandler(IPhotoRepository repository, IStorageService storage, IUnitOfWork unitOfWork)
{
    public const string StorageKeyPrefix = "collection-covers/";

    public async Task<Response<ClaimedCollectionCoverDto>> ClaimAsync(ClaimCollectionCoverRequest request, Guid ownerId,
        CancellationToken ct)
    {
        var key = request.StorageKey;
        if (ownerId == Guid.Empty || request.CollectionId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 255
            || !key.StartsWith(StorageKeyPrefix, StringComparison.Ordinal))
            return Invalid("Invalid collection cover photo claim.");

        await using var claimLocks = await repository.AcquireClaimLocksAsync(ownerId, [key], ct);
        var matches = await repository.GetClaimPhotosAsync(ownerId, [key], ct);
        var photo = matches.Count == 1 ? matches[0] : null;
        if (photo == null || photo.OwnerId != ownerId || photo.BucketType != BucketType.CheckIn
            || photo.OwnerType == OwnerType.OwnershipEvidence || photo.OwnershipClaimId != null
            || photo.CheckInId != null || photo.ShopAttachmentId != null || photo.ShelfBagId != null
            || !(photo.OriginalStorageKey ?? photo.StorageKey).StartsWith(StorageKeyPrefix, StringComparison.Ordinal))
            return Invalid("Photo was not found or does not belong to you.");
        if (photo.CollectionId is { } collectionId && collectionId != request.CollectionId)
            return Response<ClaimedCollectionCoverDto>.Error(HttpStatusCode.Conflict, "The upload is already attached to another collection.");
        if (photo.CollectionId == request.CollectionId && photo.CollectionCoverValidatedAtUtc != null
            && photo.Status is PhotoStatus.Pending or PhotoStatus.Confirmed)
        {
            if (photo.Status == PhotoStatus.Pending)
            {
                // A retry starts another Shops save, which is not visible to reconciliation until commit.
                photo.ClaimedAtUtc = DateTime.UtcNow;
                await unitOfWork.SaveChangesAsync(ct);
            }
            return Success(photo);
        }
        if (photo.Status != PhotoStatus.Pending || (photo.CollectionId == null && photo.IsExpired)
            || !PrivateCardPhotoValidation.IsAllowedContentType(photo.ContentType))
            return Invalid("Photo is no longer available or is not a JPEG, PNG or WebP.");
        // Sealing can finish (and remove the upload) before the new storage key is saved. Prefer that
        // immutable copy on retry, even if the still-valid upload URL has since replaced the original.
        var destination = CollectionCoverPhotoStorageKey.Sealed(photo.Id, photo.StorageKey);
        var uploadedSize = await storage.GetObjectSize(destination, BucketType.CheckIn, ct)
            ?? (destination == photo.StorageKey ? null : await storage.GetObjectSize(photo.StorageKey, BucketType.CheckIn, ct));
        if (uploadedSize is null or <= 0 or > PrivateCardPhotoValidation.MaxSizeBytes)
            return Invalid("Photo was not uploaded or is larger than 1 MiB.");

        if (!await repository.TryClaimCollectionCoverAsync(photo.Id, ownerId, request.CollectionId, ct))
            return Response<ClaimedCollectionCoverDto>.Error(HttpStatusCode.Conflict, "The upload is already attached to another collection.");
        photo.CollectionId = request.CollectionId;
        photo.ClaimedAtUtc = DateTime.UtcNow;
        var sealedKey = await storage.SealCollectionCoverObjectAsync(photo.StorageKey, photo.Id, ct);
        photo.OriginalStorageKey ??= photo.StorageKey;
        photo.StorageKey = sealedKey;
        await unitOfWork.SaveChangesAsync(ct);

        var size = await storage.GetObjectSize(sealedKey, BucketType.CheckIn, ct)
                   ?? throw new InvalidOperationException("Sealed collection cover photo was not found.");
        var contentType = await PrivateCardPhotoValidation.ValidateAsync(sealedKey, size, storage, ct);
        if (contentType == null)
        {
            await storage.Delete(sealedKey, BucketType.CheckIn, ct);
            photo.Status = PhotoStatus.Deleted;
            photo.DeletedAt = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync(ct);
            return Invalid("Photo must be a JPEG, PNG or WebP of at most 1 MiB and 1280 px on each side.");
        }
        photo.ContentType = contentType;
        photo.SizeBytes = size;
        photo.CollectionCoverValidatedAtUtc = DateTime.UtcNow;
        // The Discovery outbox confirms permanence only after its metadata transaction commits.
        photo.Status = PhotoStatus.Pending;
        await unitOfWork.SaveChangesAsync(ct);
        return Success(photo);
    }

    private static Response<ClaimedCollectionCoverDto> Success(PhotoMetadata photo) =>
        Response<ClaimedCollectionCoverDto>.Success(
            new ClaimedCollectionCoverDto(photo.Id, photo.StorageKey, photo.ContentType, photo.SizeBytes));

    private static Response<ClaimedCollectionCoverDto> Invalid(string message) =>
        Response<ClaimedCollectionCoverDto>.Error(HttpStatusCode.BadRequest, message);
}
