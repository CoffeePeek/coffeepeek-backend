using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public interface IFavoriteCollectionsStore
{
    Task<FavoriteCollectionDto> CreateAsync(Guid owner, CreateFavoriteCollectionRequest request, CancellationToken ct);
    Task<FavoriteCollectionDto?> GetAsync(Guid id, CancellationToken ct);
    Task<FavoriteCollectionSummary[]> ListAsync(Guid owner, CancellationToken ct);
    Task<bool> RevokeAsync(Guid owner, Guid id, CancellationToken ct);
    Task DeleteForOwnerAsync(Guid owner, CancellationToken ct);
}
