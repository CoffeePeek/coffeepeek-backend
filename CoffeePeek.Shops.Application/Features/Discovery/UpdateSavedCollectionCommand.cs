using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record UpdateSavedCollectionCommand(Guid OwnerId, Guid Id, UpdateSavedCollectionRequest Request);
