using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class CreateFavoriteCollectionHandler
{
    public static Task<FavoriteCollectionDto> Handle(CreateFavoriteCollectionCommand command, IFavoriteCollectionsStore collections,
        CancellationToken ct) => collections.CreateAsync(command.OwnerId, command.Request, ct);
}
