using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record DeleteSavedCollectionCommand(Guid OwnerId, Guid Id);
