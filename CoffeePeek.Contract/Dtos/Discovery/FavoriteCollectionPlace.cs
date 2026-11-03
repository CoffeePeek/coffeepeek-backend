namespace CoffeePeek.Contract.Dtos.Discovery;

public sealed class FavoriteCollectionPlace : DiscoveryFavoriteItem
{
    public string? CityName { get; init; }
    public string? StreetAddress { get; init; }
    public decimal? Rating { get; init; }
    public int? CheckInCount { get; init; }
}
