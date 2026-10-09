using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed class ShopMenuPhoto : Entity<Guid>
{
    public Guid ShopMenuId { get; private set; }
    public Guid? MediaPhotoId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ShopMenuPhoto()
    {
    }

    public static ShopMenuPhoto Create(
        string fileName,
        string contentType,
        string storageKey,
        long sizeBytes,
        Guid? mediaPhotoId = null)
    {
        return new ShopMenuPhoto
        {
            // The Media identity makes a retried attachment collide deterministically instead
            // of creating two menu rows when concurrent requests used the same upload.
            Id = mediaPhotoId ?? Guid.NewGuid(),
            FileName = fileName,
            ContentType = contentType,
            StorageKey = storageKey,
            SizeBytes = sizeBytes,
            MediaPhotoId = mediaPhotoId
        };
    }
}
