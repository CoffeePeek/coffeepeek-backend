using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Entities;

public sealed class PhotoMetadata : Entity<Guid>
{
    public string FileName { get; private set; } 
    public string ContentType { get; private set; } 
    public string StorageKey { get; private set; } 
    public long SizeBytes { get; private set; }
    public Guid? OwnerId { get; private set; }
    public Guid? ModerationShopId { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private PhotoMetadata() { }

    private PhotoMetadata(string fileName, string contentType, string storageKey, long sizeBytes, Guid? ownerId,
        Guid? moderationShopId)
    {
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
}