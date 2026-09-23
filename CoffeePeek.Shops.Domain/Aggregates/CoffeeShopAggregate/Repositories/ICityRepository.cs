namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface ICityRepository
{
    Task<City?> GetByIdAsync(Guid id, CancellationToken ct = default);
    void Add(City city);
    void Remove(City city);
}
