using System.Net;
using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

/// <summary>
/// Reserves one shelf-bag upload for one Shops bag. The upload must belong to the caller, be fully uploaded,
/// unused and a JPEG/PNG/WebP raster of at most 1 MiB and 1280 px per side. Retrying the same bag is idempotent.
/// </summary>
public class ClaimShelfBagPhotoHandler(IPhotoRepository repository, IStorageService storage, IUnitOfWork unitOfWork)
{
    public const string StorageKeyPrefix = "shelf-bags/";

    public async Task<Response<ClaimedShelfBagPhotoDto>> ClaimAsync(ClaimShelfBagPhotoRequest request, Guid ownerId,
        CancellationToken ct)
    {
        var key = request.StorageKey;
        if (ownerId == Guid.Empty || request.BagId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 255
            || !key.StartsWith(StorageKeyPrefix, StringComparison.Ordinal))
            return Invalid("Invalid shelf bag photo claim.");

        await using var claimLocks = await repository.AcquireClaimLocksAsync(ownerId, [key], ct);
        var matches = await repository.GetClaimPhotosAsync(ownerId, [key], ct);
        var photo = matches.Count == 1 ? matches[0] : null;
        if (photo == null || photo.OwnerId != ownerId || photo.BucketType != BucketType.CheckIn
            || photo.CheckInId != null || photo.ShopAttachmentId != null
            || !(photo.OriginalStorageKey ?? photo.StorageKey).StartsWith(StorageKeyPrefix, StringComparison.Ordinal))
            return Invalid("Photo was not found or does not belong to you.");
        if (photo.ShelfBagId is { } bagId && bagId != request.BagId)
            return Response<ClaimedShelfBagPhotoDto>.Error(HttpStatusCode.Conflict, "The upload is already attached to another bag.");
        if (photo.ShelfBagId == request.BagId && photo.Status == PhotoStatus.Confirmed)
            return Success(photo);
        if (photo.Status != PhotoStatus.Pending || (photo.ShelfBagId == null && photo.IsExpired)
            || !ShelfBagPhotoValidation.IsAllowedContentType(photo.ContentType))
            return Invalid("Photo is no longer available or is not a JPEG, PNG or WebP.");
        var uploadedSize = await storage.GetObjectSize(photo.StorageKey, BucketType.CheckIn, ct);
        if (uploadedSize is null or <= 0 or > ShelfBagPhotoValidation.MaxSizeBytes)
            return Invalid("Photo was not uploaded or is larger than 1 MiB.");

        if (!await repository.TryClaimShelfBagAsync(photo.Id, ownerId, request.BagId, ct))
            return Response<ClaimedShelfBagPhotoDto>.Error(HttpStatusCode.Conflict, "The upload is already attached to another bag.");
        photo.ShelfBagId = request.BagId;
        photo.ClaimedAtUtc ??= DateTime.UtcNow;
        var sealedKey = await storage.SealShelfBagObjectAsync(photo.StorageKey, photo.Id, ct);
        photo.OriginalStorageKey ??= photo.StorageKey;
        photo.StorageKey = sealedKey;
        await unitOfWork.SaveChangesAsync(ct);

        var size = await storage.GetObjectSize(sealedKey, BucketType.CheckIn, ct)
                   ?? throw new InvalidOperationException("Sealed shelf bag photo was not found.");
        var contentType = await ShelfBagPhotoValidation.ValidateAsync(sealedKey, size, storage, ct);
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
        photo.Status = PhotoStatus.Confirmed;
        await unitOfWork.SaveChangesAsync(ct);
        return Success(photo);
    }

    private static Response<ClaimedShelfBagPhotoDto> Success(PhotoMetadata photo) =>
        Response<ClaimedShelfBagPhotoDto>.Success(
            new ClaimedShelfBagPhotoDto(photo.Id, photo.StorageKey, photo.ContentType, photo.SizeBytes));

    private static Response<ClaimedShelfBagPhotoDto> Invalid(string message) =>
        Response<ClaimedShelfBagPhotoDto>.Error(HttpStatusCode.BadRequest, message);
}
