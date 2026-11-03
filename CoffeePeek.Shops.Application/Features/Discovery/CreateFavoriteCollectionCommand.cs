using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record CreateFavoriteCollectionCommand(Guid OwnerId, CreateFavoriteCollectionRequest Request);
