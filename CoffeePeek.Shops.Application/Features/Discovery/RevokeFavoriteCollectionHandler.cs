namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class RevokeFavoriteCollectionHandler
{
    public static Task<bool> Handle(RevokeFavoriteCollectionCommand command, IFavoriteCollectionsStore collections,
        CancellationToken ct) => collections.RevokeAsync(command.OwnerId, command.CollectionId, ct);
}
