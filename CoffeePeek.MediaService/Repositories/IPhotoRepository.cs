using CoffeePeek.MediaService.Data;

namespace CoffeePeek.MediaService.Repositories;

public interface IPhotoRepository
{
    void Add(PhotoMetadata metadata);
    void AddRange(IEnumerable<PhotoMetadata> photoMetadatas);
    Task<PhotoMetadata?> GetByIdAsync(Guid commandPhotoId, CancellationToken ct = default);

    Task<List<PhotoMetadata>> GetByStorageKeysAsync(
        Guid ownerId,
        IReadOnlyCollection<string> storageKeys,
        CancellationToken ct = default);

    Task<bool> TryClaimAsync(Guid photoId, Guid ownerId, Guid checkInId, CancellationToken ct = default);
    Task<IAsyncDisposable> AcquireClaimLocksAsync(Guid ownerId, IReadOnlyCollection<string> storageKeys, CancellationToken ct = default);
    Task<List<PhotoMetadata>> GetClaimPhotosAsync(Guid ownerId, IReadOnlyCollection<string> storageKeys, CancellationToken ct = default);
    Task ReleaseClaimAsync(Guid ownerId, Guid checkInId, CancellationToken ct = default);
    Task<PhotoMetadata?> GetCheckInPhotoAsync(Guid checkInId, Guid photoId, Guid ownerId, CancellationToken ct = default);
}
