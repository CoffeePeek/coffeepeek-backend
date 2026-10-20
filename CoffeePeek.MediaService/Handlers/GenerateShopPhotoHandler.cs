using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

public static class GenerateShopPhotoHandler
{
    public static bool IsSafePhotoContentType(string? contentType) => contentType?.ToLowerInvariant() is
        "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/bmp" or "image/avif";

    public static async Task<(Response<List<GenerateUploadUrlResponse>>, PhotosUploadedEvent)> Handle(
        GenerateShopPhotosCommand command,
        IStorageService storageService,
        IPhotoRepository repository,
        CancellationToken ct)
    {
        if (command.Requests is null || command.Requests.Any(request => request is null
                || !IsSafePhotoContentType(request.ContentType)))
            return (Response<List<GenerateUploadUrlResponse>>.Error("Only JPEG, PNG, GIF, WebP, BMP and AVIF photos are supported."),
                new PhotosUploadedEvent([]));

        var metadataList = new List<PhotoMetadata>();
        var results = new List<GenerateUploadUrlResponse>();

        foreach (var request in command.Requests)
        {
            var presigned = await storageService
                .GetPresignedUploadUrl(
                    request.FileName,
                    request.ContentType.ToLowerInvariant(),
                    command.Bucket,
                    ct,
                    command.StorageKeyPrefix);

            var metadata = PhotoMetadata.Pending(
                request.FileName, request.ContentType.ToLowerInvariant(), presigned.StorageKey,
                request.SizeBytes, command.Bucket, OwnerType.Shop, command.OwnerId);

            metadataList.Add(metadata);
            results.Add(new GenerateUploadUrlResponse(metadata.Id, presigned.Url, metadata.StorageKey));
        }

        repository.AddRange(metadataList);

        var photosEvent = new PhotosUploadedEvent(metadataList.Select(e => new PhotoUploadedEvent(
            e.Id,
            e.StorageKey,
            e.FileName,
            e.ContentType,
            e.SizeBytes,
            e.OwnerType.ToString(),
            e.OwnerId,
            DateTime.UtcNow)));

        return (Response<List<GenerateUploadUrlResponse>>.Success(results), photosEvent);
    }
}
