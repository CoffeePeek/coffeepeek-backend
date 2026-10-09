using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public sealed class CheckInPhoto : Entity<Guid>
{
    [MaxLength(255)] public string FileName { get; private set; } = null!;
    [MaxLength(100)] public string ContentType { get; private set; } = null!;
    [MaxLength(255)] public string StorageKey { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public Guid OwnerId { get; private set; }
    public int SortIndex { get; private set; }
    public Guid CanonicalCheckInId { get; private set; }

    private CheckInPhoto() { }

    public CheckInPhoto(string fileName, string contentType, string storageKey, long sizeBytes,
        Guid ownerId, int sortIndex = 0, Guid? id = null)
    {
        if (ownerId == Guid.Empty || id == Guid.Empty || sortIndex < 0 || sizeBytes <= 0
            || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255
            || string.IsNullOrWhiteSpace(contentType) || contentType.Length > 100
            || string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 255)
            throw new DomainException("Check-in photo metadata is invalid.");
        Id = id ?? Guid.NewGuid();
        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        OwnerId = ownerId;
        SortIndex = sortIndex;
    }

    internal void AttachTo(Guid checkInId) => CanonicalCheckInId = checkInId;
}
