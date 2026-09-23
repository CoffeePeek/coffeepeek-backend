using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Entities;

public sealed class RoasterPhoto : Entity<Guid>
{
    [MaxLength(BusinessConstants.MaxRoasterPhotoFileNameLength)]
    public string FileName { get; private set; }

    [MaxLength(BusinessConstants.MaxRoasterPhotoContentTypeLength)]
    public string ContentType { get; private set; }

    [MaxLength(BusinessConstants.MaxRoasterPhotoStorageKeyLength)]
    public string StorageKey { get; private set; }

    public long SizeBytes { get; private set; }
    public Guid OwnerId { get; private set; }

    /// <summary>0-based display order within the roaster's photo gallery.</summary>
    public int SortIndex { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private RoasterPhoto() { }

    public RoasterPhoto(string fileName, string contentType, string storageKey, long sizeBytes, Guid ownerId, int sortIndex = 0)
    {
        Id = Guid.NewGuid();
        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        OwnerId = ownerId;
        SetSortIndex(sortIndex);
    }

    public void SetSortIndex(int sortIndex)
    {
        if (sortIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sortIndex), "SortIndex cannot be negative.");

        SortIndex = sortIndex;
    }
}
