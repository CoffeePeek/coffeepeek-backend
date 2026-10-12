using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public sealed class ModerationRoasterPhoto : Entity<Guid>
{
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public Guid? OwnerId { get; private set; }
    public Guid? ModerationRoasterId { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ModerationRoasterPhoto() { }

    private ModerationRoasterPhoto(
        string fileName, string contentType, string storageKey, long sizeBytes, Guid? ownerId, Guid? moderationRoasterId)
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
        ModerationRoasterId = moderationRoasterId is null || moderationRoasterId == Guid.Empty
            ? null
            : moderationRoasterId;
    }

    public static ModerationRoasterPhoto Create(
        string fileName, string contentType, string storageKey, long sizeBytes, Guid? ownerId, Guid? moderationRoasterId) =>
        new(fileName, contentType, storageKey, sizeBytes, ownerId, moderationRoasterId);
}
