namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public record AdminDownloadAggregate(
    int Total,
    int SinceCount,
    IReadOnlyList<(string Channel, int Total, int SinceCount)> ByChannel,
    IReadOnlyList<(string Country, int Count)> TopCountriesSince);
