using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Entities;

public sealed class PhotoMetadata : Entity<Guid>
{
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public Guid? OwnerId { get; private set; }
    public Guid? ModerationShopId { get; private set; }
    public Guid? CheckInPhotoId { get; private set; }
    public int SortIndex { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private PhotoMetadata() { }

    private PhotoMetadata(string fileName, string contentType, string storageKey, long sizeBytes, Guid? ownerId,
        Guid? moderationShopId)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentType)
            || string.IsNullOrWhiteSpace(storageKey) || sizeBytes <= 0)
            throw new CoffeePeek.Shared.Kernel.Exceptions.DomainException("Invalid photo metadata.");
        Id = Guid.NewGuid();
        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        OwnerId = ownerId;
        ModerationShopId = moderationShopId is null || moderationShopId == Guid.Empty
            ? null
            : moderationShopId;
    }

    public static PhotoMetadata Create(string fileName, string contentType, string storageKey, long sizeBytes, Guid? ownerId, Guid? moderationShopId)
    {
        return new PhotoMetadata(fileName, contentType, storageKey, sizeBytes, ownerId, moderationShopId);
    }

    public static PhotoMetadata CreateCheckInPhoto(Guid checkInPhotoId, int sortIndex, string fileName,
        string contentType, string storageKey, long sizeBytes, Guid ownerId)
    {
        if (checkInPhotoId == Guid.Empty || sortIndex < 0 || sizeBytes <= 0 || string.IsNullOrWhiteSpace(storageKey))
            throw new CoffeePeek.Shared.Kernel.Exceptions.DomainException("Invalid check-in photo metadata.");
        return new PhotoMetadata(fileName, contentType, storageKey, sizeBytes, ownerId, null)
        {
            CheckInPhotoId = checkInPhotoId,
            SortIndex = sortIndex
        };
    }
}
