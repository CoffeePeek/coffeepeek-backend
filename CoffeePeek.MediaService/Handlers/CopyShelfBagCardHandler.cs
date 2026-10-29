using System.Net;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.MediaService.Handlers;

/// <summary>
/// Copies a confirmed shelf-bag card into a new check-in photo reserved for one check-in, like a claimed upload.
/// The copy has its own identity and bytes, so it follows check-in read rules and outlives the card.
/// Retrying the same copy ID returns the same copy.
/// </summary>
public class CopyShelfBagCardHandler(IPhotoRepository repository, IStorageService storage, IUnitOfWork unitOfWork)
{
    private const string FileName = "shelf-bag";

    public async Task<Response<ClaimedCheckInPhotoDto>> CopyAsync(CopyShelfBagCardRequest request, Guid ownerId,
        CancellationToken ct)
    {
        if (ownerId == Guid.Empty || request.CheckInId == Guid.Empty || request.BagId == Guid.Empty
            || request.PhotoId == Guid.Empty
            || request.CopyId == Guid.Empty || request.CopyId == request.PhotoId)
            return Response<ClaimedCheckInPhotoDto>.Error(HttpStatusCode.BadRequest, "Invalid shelf bag card copy.");
        var card = await repository.GetShelfBagPhotoAsync(request.BagId, request.PhotoId, ownerId, ct);
        if (card == null)
            return Response<ClaimedCheckInPhotoDto>.Error(HttpStatusCode.NotFound, "Shelf bag card not found.");

        var copyId = request.CopyId;
        var existing = await repository.GetByIdAsync(copyId, ct);
        if (existing != null)
        {
            if (existing.OwnerId != ownerId || existing.Status is not (PhotoStatus.Pending or PhotoStatus.Confirmed)
                || existing.CheckInId is { } checkInId && checkInId != request.CheckInId)
                return Response<ClaimedCheckInPhotoDto>.Error(HttpStatusCode.Conflict, "The card copy is no longer available.");
            existing.CheckInId = request.CheckInId;
            existing.ClaimedAtUtc ??= DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync(ct);
            return Success(existing);
        }

        await storage.EnsureCheckInBucketPrivateAsync(ct);
        var key = $"check-ins/{copyId:N}{Path.GetExtension(card.StorageKey)}";
        if (await storage.GetObjectSize(key, BucketType.CheckIn, ct) == null)
        {
            // Cards are at most 1 MiB, so one buffered copy is cheaper than a second storage primitive.
            using var bytes = new MemoryStream();
            await storage.CopyToAsync(card.StorageKey, BucketType.CheckIn, bytes, ct);
            bytes.Position = 0;
            await storage.Put(key, card.ContentType, bytes, bytes.Length, BucketType.CheckIn, ct);
        }
        var size = await storage.GetObjectSize(key, BucketType.CheckIn, ct)
                   ?? throw new InvalidOperationException("Shelf bag card copy was not stored.");
        var now = DateTime.UtcNow;
        var copy = new PhotoMetadata
        {
            Id = copyId, FileName = FileName + Path.GetExtension(card.StorageKey), ContentType = card.ContentType,
            StorageKey = key, OriginalStorageKey = key, SizeBytes = size, UploadedAt = now,
            Status = PhotoStatus.Pending, BucketType = BucketType.CheckIn, OwnerType = card.OwnerType, OwnerId = ownerId,
            CheckInId = request.CheckInId, ClaimedAtUtc = now
        };
        repository.Add(copy);
        await unitOfWork.SaveChangesAsync(ct);
        return Success(copy);
    }

    private static Response<ClaimedCheckInPhotoDto> Success(PhotoMetadata photo) =>
        Response<ClaimedCheckInPhotoDto>.Success(new ClaimedCheckInPhotoDto(photo.Id, photo.StorageKey, photo.StorageKey,
            photo.FileName, photo.ContentType, photo.SizeBytes));
}
