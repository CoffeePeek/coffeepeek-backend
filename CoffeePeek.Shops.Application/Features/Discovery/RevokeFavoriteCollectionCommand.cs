namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record RevokeFavoriteCollectionCommand(Guid OwnerId, Guid CollectionId);
