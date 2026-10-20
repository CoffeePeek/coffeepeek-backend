using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

public static class GenerateAvatarUploadHandler
{
    public static async Task<(Response<GenerateUploadUrlResponse>, PhotoUploadedEvent)> Handle(
        GenerateAvatarUploadCommand command,
        IStorageService storageService,
        IPhotoRepository repository,
        CancellationToken ct)
    {
        if (command.SizeBytes > 5 * 1024 * 1024)
            throw new ValidationException("File size should be less than 5MB");

        var presigned = await storageService
            .GetPresignedUploadUrl(command.FileName, command.ContentType, BucketType.User, ct);

        var metadata = PhotoMetadata.Pending(
            command.FileName, command.ContentType, presigned.StorageKey,
            command.SizeBytes, BucketType.User, OwnerType.User, command.OwnerId);

        repository.Add(metadata);

        var uploadedEvent = new PhotoUploadedEvent(
            metadata.Id,
            metadata.StorageKey,
            metadata.FileName,
            metadata.ContentType,
            metadata.SizeBytes,
            metadata.OwnerType.ToString(),
            metadata.OwnerId,
            DateTime.UtcNow);

        var response = Response<GenerateUploadUrlResponse>.Success(
            new GenerateUploadUrlResponse(metadata.Id, presigned.Url, metadata.StorageKey));

        return (response, uploadedEvent);
    }
}