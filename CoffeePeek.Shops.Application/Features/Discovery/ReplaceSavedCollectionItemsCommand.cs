using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record ReplaceSavedCollectionItemsCommand(Guid OwnerId, Guid Id, ReplaceSavedCollectionItemsRequest Request);
