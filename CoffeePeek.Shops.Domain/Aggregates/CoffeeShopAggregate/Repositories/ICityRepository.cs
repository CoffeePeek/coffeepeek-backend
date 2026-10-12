namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface ICityRepository
{
    Task<City?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(City city);
    Task<CityDeleteResult> DeleteIfUnusedAsync(Guid id, CancellationToken ct = default);
}
