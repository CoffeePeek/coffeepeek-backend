using System.ComponentModel.DataAnnotations;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.Contract.Dtos.ShopPhotos;

namespace CoffeePeek.MediaService.Data;

public class PhotoMetadata
{
    public Guid Id { get; set; }

    [MaxLength(255)]
    public string FileName { get; set; } = null!;

    [MaxLength(100)]
    public string ContentType { get; set; } = null!;

    [MaxLength(255)]
    public string StorageKey { get; set; } = null!;

    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; }

    public PhotoStatus Status { get; set; }

    public BucketType BucketType { get; set; }

    public OwnerType OwnerType { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? OwnershipClaimId { get; set; }
    public Guid? CheckInId { get; set; }
    /// <summary>Shops shelf bag that owns this private card; set when Shops claims a shelf-bags/ upload.</summary>
    public Guid? ShelfBagId { get; set; }
    public Guid? ShopAttachmentId { get; set; }
    public ShopPhotoAttachmentKind? ShopAttachmentKind { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    [MaxLength(255)]
    public string? OriginalStorageKey { get; set; }

    /// <summary>
    /// When the photo was actually deleted from storage.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public bool IsExpired => Status == PhotoStatus.Pending && UploadedAt < DateTime.UtcNow.AddHours(-24);

    public static PhotoMetadata Pending(string fileName, string contentType, string storageKey, long sizeBytes,
        BucketType bucketType, OwnerType ownerType, Guid ownerId) => new()
    {
        Id = Guid.NewGuid(), FileName = fileName, ContentType = contentType, StorageKey = storageKey,
        SizeBytes = sizeBytes, BucketType = bucketType, OwnerType = ownerType, OwnerId = ownerId,
        Status = PhotoStatus.Pending, UploadedAt = DateTime.UtcNow
    };
}
