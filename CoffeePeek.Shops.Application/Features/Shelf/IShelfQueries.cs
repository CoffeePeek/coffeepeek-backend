using CoffeePeek.Contract.Dtos.Shelf;

namespace CoffeePeek.Shops.Application.Features.Shelf;

public interface IShelfQueries
{
    Task<ShelfBagDto[]> GetShelfAsync(Guid userId, CancellationToken ct);
    Task<ShelfArchivePage> GetArchiveAsync(Guid userId, int pageNumber, int pageSize, string? query, CancellationToken ct);
    /// <summary>The bag's current card, only for its owner.</summary>
    Task<ShelfBagPhotoAccess?> GetPhotoAsync(Guid userId, Guid bagId, Guid photoId, CancellationToken ct);
}
