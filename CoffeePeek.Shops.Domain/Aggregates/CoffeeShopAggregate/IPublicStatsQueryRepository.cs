namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IPublicStatsQueryRepository
{
    Task<PublicPlatformStats> GetStatsAsync(CancellationToken cancellationToken = default);
}
