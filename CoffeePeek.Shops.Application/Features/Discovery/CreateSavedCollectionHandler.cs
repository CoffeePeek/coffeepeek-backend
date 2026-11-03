using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class CreateSavedCollectionHandler
{
    public static Task<SavedCollectionDto> Handle(CreateSavedCollectionCommand command, ISavedCollectionsStore collections, CancellationToken ct) =>
        collections.CreateAsync(command.OwnerId, command.Request, ct);
}
