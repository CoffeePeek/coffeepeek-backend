using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class ReplaceSavedCollectionItemsHandler
{
    public static Task<SavedCollectionDto?> Handle(ReplaceSavedCollectionItemsCommand command, ISavedCollectionsStore collections, CancellationToken ct) =>
        collections.ReplaceItemsAsync(command.OwnerId, command.Id, command.Request, ct);
}
