using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public sealed class ModerationRoasterPhoto : Entity<Guid>
{
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public string StorageKey { get; private set; }
    public long SizeBytes { get; private set; }
    public Guid? OwnerId { get; private set; }
    public Guid? ModerationRoasterId { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ModerationRoasterPhoto() { }

    private ModerationRoasterPhoto(
        string fileName, string contentType, string storageKey, long sizeBytes, Guid? ownerId, Guid? moderationRoasterId)
    {
        Id = Guid.NewGuid();
        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        OwnerId = ownerId;
        ModerationRoasterId = moderationRoasterId is null || moderationRoasterId == Guid.Empty
            ? null
            : moderationRoasterId;
    }

    public static ModerationRoasterPhoto Create(
        string fileName, string contentType, string storageKey, long sizeBytes, Guid? ownerId, Guid? moderationRoasterId) =>
        new(fileName, contentType, storageKey, sizeBytes, ownerId, moderationRoasterId);
}
