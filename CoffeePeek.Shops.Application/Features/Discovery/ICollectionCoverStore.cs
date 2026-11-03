using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public interface ICollectionCoverStore
{
    Task<ClaimedCollectionCoverDto> ClaimAsync(Guid owner, Guid collectionId, string storageKey, CancellationToken ct);
}
