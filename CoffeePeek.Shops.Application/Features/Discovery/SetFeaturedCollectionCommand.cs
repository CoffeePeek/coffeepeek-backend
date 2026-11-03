using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record SetFeaturedCollectionCommand(Guid OwnerId, Guid? Id);
