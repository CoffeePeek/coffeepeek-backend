using CoffeePeek.Contract.Dtos;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

public static class ResolveUploadedPhotosHandler
{
    /// <summary>Hard sanity cap on a single resolve call; callers enforce their own (lower) limits.</summary>
    public const int MaxStorageKeysPerRequest = 20;

    public static async Task<Response<List<ResolvedUploadedPhotoDto>>> Handle(
        ResolveUploadedPhotosQuery query,
        IPhotoRepository repository,
        IStorageService storageService,
        CancellationToken ct)
    {
        if (query.StorageKeys is null || query.StorageKeys.Count == 0)
            return Response<List<ResolvedUploadedPhotoDto>>.Error("At least one storage key is required");

        if (query.StorageKeys.Count > MaxStorageKeysPerRequest)
            return Response<List<ResolvedUploadedPhotoDto>>.Error(
                $"At most {MaxStorageKeysPerRequest} storage keys can be resolved at once");

        var keys = query.StorageKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var photos = await repository.GetByStorageKeysAsync(query.OwnerId, keys, ct);

        // Read-only: the check-in can still fail after resolve, so nothing is confirmed here.
        // The in-memory filter repeats the owner check as defense in depth.
        var candidates = photos.Where(p =>
            p.OwnerId == query.OwnerId
            && p.BucketType == BucketType.Shop
            && p.Status == PhotoStatus.Pending
            && !p.IsExpired);

        var result = new List<ResolvedUploadedPhotoDto>();
        foreach (var photo in candidates)
        {
            // The client never uploaded (or the upload failed): nothing to attach.
            var size = await storageService.GetObjectSize(photo.StorageKey, BucketType.Shop, ct);
            if (size is null) continue;

            result.Add(new ResolvedUploadedPhotoDto(photo.StorageKey, photo.FileName, photo.ContentType, size.Value));
        }

        return Response<List<ResolvedUploadedPhotoDto>>.Success(result);
    }
}
