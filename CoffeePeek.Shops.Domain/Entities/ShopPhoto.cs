using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Entities;

public sealed class ShopPhoto : Entity<Guid>
{
    [MaxLength(255)]
    public string FileName { get; private set; }
    [MaxLength(100)]
    public string ContentType { get; private set; }
    [MaxLength(255)]
    public string StorageKey { get; private set; }

    public long SizeBytes { get; private set; }
    public Guid OwnerId { get; private set; }

    /// <summary>0-based display order within the shop gallery or historical Community archive.</summary>
    public int SortIndex { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ShopPhoto() { }

    public ShopPhoto(string fileName, string contentType, string storageKey, long sizeBytes, Guid ownerId, int sortIndex = 0, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        OwnerId = ownerId;
        SetSortIndex(sortIndex);
    }

    /// <summary>Sets display order. Callers must pass a non-negative index (validated).</summary>
    internal void SetSortIndex(int sortIndex)
    {
        if (sortIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sortIndex), "SortIndex cannot be negative.");

        SortIndex = sortIndex;
    }
}
