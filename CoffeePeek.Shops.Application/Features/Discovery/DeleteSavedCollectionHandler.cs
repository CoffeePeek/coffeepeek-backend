using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class DeleteSavedCollectionHandler
{
    public static Task<bool> Handle(DeleteSavedCollectionCommand command, ISavedCollectionsStore collections, CancellationToken ct) =>
        collections.DeleteAsync(command.OwnerId, command.Id, ct);
}
