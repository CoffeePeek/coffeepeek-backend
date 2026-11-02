namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class FavoriteCollectionPlace : DiscoveryFavoriteItem
{
    public string? CityName { get; init; }
    public string? StreetAddress { get; init; }
}
