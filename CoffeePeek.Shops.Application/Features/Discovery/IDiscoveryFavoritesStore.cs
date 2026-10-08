using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public interface IDiscoveryFavoritesStore
{
    Task<DiscoveryFavoriteItem[]> ListAsync(Guid userId, CancellationToken ct);
    Task SetAsync(Guid userId, string kind, string slug, bool favorite, CancellationToken ct);
}
