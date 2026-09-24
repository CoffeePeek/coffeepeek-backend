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
}