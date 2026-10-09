namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class SetDiscoveryFavoriteHandler
{
    public static Task Handle(SetDiscoveryFavoriteCommand command, IDiscoveryFavoritesStore favorites, CancellationToken ct) =>
        favorites.SetAsync(command.UserId, command.Kind, command.Slug, command.Favorite, ct);
}
