namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record CollectionCoverAccess(Guid OwnerId, Guid CollectionId, Guid PhotoId, string ContentType);
