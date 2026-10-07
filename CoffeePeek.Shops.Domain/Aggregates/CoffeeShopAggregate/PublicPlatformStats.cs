namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record PublicPlatformStats(
    int TotalCoffeeShops,
    int TotalCheckIns,
    decimal AverageRating);
