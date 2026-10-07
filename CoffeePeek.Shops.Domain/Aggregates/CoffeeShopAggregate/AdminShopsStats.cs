namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record AdminShopsStats(
    int TotalCoffeeShops,
    int NewCoffeeShopsToday,
    int TotalCheckIns,
    int NewCheckInsToday);
