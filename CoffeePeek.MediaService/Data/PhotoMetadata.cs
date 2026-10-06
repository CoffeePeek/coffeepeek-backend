using System.ComponentModel.DataAnnotations;
using CoffeePeek.MediaService.Configuration;

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

    public DateTime? PermalinkExpiresAt { get; set; }

    /// <summary>
    /// When the photo is scheduled for deletion (for replaced photos).
    /// </summary>
    public DateTime? ScheduledDeletionAt { get; set; }

    /// <summary>
    /// When the photo was actually deleted from storage.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public bool IsExpired => Status == PhotoStatus.Pending && UploadedAt < DateTime.UtcNow.AddHours(-24);

    /// <summary>
    /// Checks if this photo is scheduled for deletion and the delay has passed.
    /// </summary>
    public bool IsReadyForDeletion(DateTime utcNow) =>
        Status == PhotoStatus.PendingDeletion &&
        ScheduledDeletionAt.HasValue &&
        ScheduledDeletionAt.Value <= utcNow;
}
