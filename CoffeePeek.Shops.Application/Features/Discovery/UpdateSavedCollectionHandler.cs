using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class UpdateSavedCollectionHandler
{
    public static Task<SavedCollectionDto?> Handle(UpdateSavedCollectionCommand command, ISavedCollectionsStore collections, CancellationToken ct) =>
        collections.UpdateAsync(command.OwnerId, command.Id, command.Request, ct);
}
