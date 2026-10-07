namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

/// <param name="StarCounts">Review count per star (1..5), always 5 entries.</param>
public record AdminRatingAggregate(
    int TotalCheckIns,
    decimal AverageRating,
    decimal AveragePlace,
    decimal AverageService,
    decimal AverageCoffee,
    IReadOnlyList<(int Stars, int Count)> StarCounts);
