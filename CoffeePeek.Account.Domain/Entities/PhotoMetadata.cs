using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Domain.Entities;

public class PhotoMetadata : Entity<Guid>
{
    [MaxLength(255)]
    public string FileName { get; private set; } = null!;
    [MaxLength(50)]
    public string ContentType { get; private set; } = null!;
    [MaxLength(255)]
    public string StorageKey { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public DateTime UploadedAt { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private PhotoMetadata() { }

    private PhotoMetadata(string fileName, string contentType, string storageKey, long length)
    {
        Id = Guid.NewGuid();
        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = length;
        UploadedAt = DateTime.UtcNow;
    }

    public static PhotoMetadata Create(string fileName, string contentType, string storageKey, long length)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new DomainException("File name cannot be empty.");
            
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new DomainException("Storage key is required.");

        if (length <= 0)
            throw new DomainException("File size must be positive.");

        return new PhotoMetadata(fileName, contentType, storageKey, length);
    }
}