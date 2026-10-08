namespace CoffeePeek.Contract.Dtos.Public;

public record PublicPlatformStatsDto(
    int TotalCoffeeShops,
    int TotalCheckIns,
    decimal AverageRating);
