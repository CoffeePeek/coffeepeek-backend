namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record SetDiscoveryFavoriteCommand(Guid UserId, string Kind, string Slug, bool Favorite);
