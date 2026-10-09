using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

/// <summary>
/// Claims survive a lost HTTP response or uncertain Shops commit. Retrying the same shop/kind
/// returns the same upload; a different destination cannot take the reservation.
/// </summary>
public sealed class ClaimShopPhotosHandler(IPhotoRepository repository, IStorageService storage)
{
    public async Task<Response<List<ClaimedShopPhotoDto>>> ClaimAsync(
        ClaimShopPhotosRequest request, Guid ownerId, CancellationToken ct)
    {
        if (ownerId == Guid.Empty || request.ShopId == Guid.Empty
            || !Enum.IsDefined(request.Kind)
            || request.StorageKeys is not { Count: > 0 and <= 20 }
            || request.StorageKeys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 255)
            || request.StorageKeys.Distinct(StringComparer.Ordinal).Count() != request.StorageKeys.Count)
            return Response<List<ClaimedShopPhotoDto>>.Error("Invalid shop photo claim.");

        await using var locks = await repository.AcquireClaimLocksAsync(ownerId, request.StorageKeys, ct);
        var photos = await repository.GetClaimPhotosAsync(ownerId, request.StorageKeys, ct);
        var ordered = request.StorageKeys.Select(key =>
        {
            var matches = photos.Where(p => p.OwnerId == ownerId && p.StorageKey == key).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }).ToArray();
        if (ordered.Any(p => p is null
                || !GenerateShopPhotoHandler.IsSafePhotoContentType(p.ContentType)
                || p.BucketType != BucketType.Shop || p.CheckInId != null
                || (p.ShopAttachmentId == null && (p.Status != PhotoStatus.Pending || p.IsExpired))
                || (p.ShopAttachmentId != null &&
                    (p.ShopAttachmentId != request.ShopId || p.ShopAttachmentKind != request.Kind
                     || p.Status is not (PhotoStatus.Pending or PhotoStatus.Confirmed)))))
            return Response<List<ClaimedShopPhotoDto>>.Error("Photos were not found, owned or unused.");

        var sizes = new long[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var size = await storage.GetObjectSize(ordered[i]!.StorageKey, BucketType.Shop, ct)
                ?? throw new InvalidOperationException("Photo was not uploaded.");
            if (size <= 0) throw new InvalidOperationException("Uploaded photo is empty.");
            sizes[i] = size;
        }

        if (!await repository.TryClaimShopBatchAsync(ordered.Select(p => p!.Id).ToArray(),
                ownerId, request.ShopId, request.Kind, ct))
            throw new InvalidOperationException("A photo was claimed by another destination.");

        var result = ordered.Select((photo, i) => new ClaimedShopPhotoDto(photo!.Id,
            photo.StorageKey, photo.FileName, photo.ContentType, sizes[i])).ToList();

        return Response<List<ClaimedShopPhotoDto>>.Success(result);
    }
}
