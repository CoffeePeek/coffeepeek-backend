using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record CreateSavedCollectionCommand(Guid OwnerId, CreateSavedCollectionRequest Request);
