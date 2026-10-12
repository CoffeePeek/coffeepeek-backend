using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.MediaService.Data;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

public class ClaimCheckInPhotosHandler(IPhotoRepository repository, IStorageService storage, IUnitOfWork unitOfWork)
{
    public async Task<Response<List<ClaimedCheckInPhotoDto>>> ClaimAsync(
        ClaimCheckInPhotosRequest request, Guid ownerId, CancellationToken ct)
    {
        if (ownerId == Guid.Empty || request.CheckInId == Guid.Empty || request.StorageKeys is not { Length: > 0 }
            || request.StorageKeys.Length > (request.DrinkPhotoStorageKey == null ? 5 : 6)
            || (request.DrinkPhotoStorageKey != null && (request.StorageKeys.Length < 2
                || !request.StorageKeys.Contains(request.DrinkPhotoStorageKey, StringComparer.Ordinal)))
            || request.StorageKeys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 255)
            || request.StorageKeys.Distinct(StringComparer.Ordinal).Count() != request.StorageKeys.Length)
            return Response<List<ClaimedCheckInPhotoDto>>.Error("Invalid check-in photo claim.");

        await using var claimLocks = await repository.AcquireClaimLocksAsync(ownerId, request.StorageKeys, ct);
        var photos = await repository.GetClaimPhotosAsync(ownerId, request.StorageKeys, ct);
        var ordered = request.StorageKeys.Select(key =>
        {
            var matches = photos.Where(p => p.OwnerId == ownerId
                && (p.StorageKey == key || p.OriginalStorageKey == key)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }).ToArray();
        if (ordered.Any(p => p == null || !GenerateShopPhotoHandler.IsSafePhotoContentType(p.ContentType)
                || p.BucketType is not (BucketType.Shop or BucketType.CheckIn)
                || p.ShopAttachmentId != null
                || (p.CheckInId != null && p.CheckInId != request.CheckInId)
                || (p.CheckInId == null && (p.Status != PhotoStatus.Pending || p.IsExpired))
                || (p.CheckInId == request.CheckInId && p.Status is not (PhotoStatus.Pending or PhotoStatus.Confirmed)))
            || ordered.Select(p => p!.Id).Distinct().Count() != ordered.Length)
            return Response<List<ClaimedCheckInPhotoDto>>.Error("Photos were not found, owned or unused.");

        // Same upload-set retries share a canonical ID. Retain ambiguous reservations;
        // a failing attempt must never compensate a concurrent successful create.
        {
            await storage.EnsureCheckInBucketPrivateAsync(ct);
            foreach (var photo in ordered)
                if (!await repository.TryClaimAsync(photo!.Id, ownerId, request.CheckInId, ct))
                    throw new InvalidOperationException("Photo was claimed by another check-in.");

            var result = new List<ClaimedCheckInPhotoDto>();
            for (var i = 0; i < ordered.Length; i++)
            {
                var photo = ordered[i]!;
                photo.CheckInId = request.CheckInId;
                photo.ClaimedAtUtc = DateTime.UtcNow;
                if (photo.OriginalStorageKey == null || photo.BucketType == BucketType.Shop)
                {
                    photo.OriginalStorageKey ??= photo.StorageKey;
                    photo.StorageKey = await storage.MoveToCheckInStorageAsync(photo.StorageKey, photo.Id, request.CheckInId, ct, photo.BucketType);
                    photo.BucketType = BucketType.CheckIn;
                    await unitOfWork.SaveChangesAsync(ct);
                }
                var size = await storage.GetObjectSize(photo.StorageKey, BucketType.CheckIn, ct)
                    ?? throw new InvalidOperationException("Photo was not uploaded.");
                if (request.StorageKeys[i] == request.DrinkPhotoStorageKey)
                {
                    // Validate the immutable copy, not a live upload URL that can still replace bytes.
                    if (!await DrinkPhotoValidation.IsValidAsync(photo, size, storage, ct))
                        return Response<List<ClaimedCheckInPhotoDto>>.Error("DrinkPhoto must be a transparent PNG up to 768px and 4 MiB.");
                }
                photo.SizeBytes = size;
                result.Add(new ClaimedCheckInPhotoDto(photo.Id, request.StorageKeys[i], photo.StorageKey,
                    photo.FileName, photo.ContentType, size));
            }
            await unitOfWork.SaveChangesAsync(ct);
            return Response<List<ClaimedCheckInPhotoDto>>.Success(result);
        }
    }
}
