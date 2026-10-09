using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed class CoffeeProductPhoto : Entity<Guid>
{
    public Guid CoffeeProductSourceId { get; private set; }
    public string SourceUrl { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public Guid MediaPhotoId { get; private set; }
    public int SortIndex { get; private set; }
    private CoffeeProductPhoto() { }
    public CoffeeProductPhoto(string storageKey, string sourceUrl, string fileName, string contentType,
        long sizeBytes, Guid mediaPhotoId, int sortIndex = 0)
    {
        CoffeeProductSource.ValidateIdentity(storageKey, 255);
        Id = Guid.NewGuid(); StorageKey = storageKey;
        UpdateMetadata(sourceUrl, fileName, contentType, sizeBytes, mediaPhotoId, sortIndex);
    }

    internal void AttachTo(Guid sourceId)
    {
        if (sourceId == Guid.Empty || CoffeeProductSourceId != Guid.Empty && CoffeeProductSourceId != sourceId)
            throw new DomainException("Photo belongs to a different source.");
        CoffeeProductSourceId = sourceId;
    }

    internal void UpdateMetadata(string sourceUrl, string fileName, string contentType,
        long sizeBytes, Guid mediaPhotoId, int sortIndex)
    {
        CoffeeProductSource.ValidateUrl(sourceUrl);
        CoffeeProductSource.ValidateIdentity(fileName, 255);
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif") ||
            sizeBytes is <= 0 or > 8388608 || mediaPhotoId == Guid.Empty || sortIndex < 0)
            throw new DomainException("Invalid coffee photo metadata.");
        SourceUrl = sourceUrl; FileName = fileName; ContentType = contentType;
        SizeBytes = sizeBytes; MediaPhotoId = mediaPhotoId; SortIndex = sortIndex;
    }
}
