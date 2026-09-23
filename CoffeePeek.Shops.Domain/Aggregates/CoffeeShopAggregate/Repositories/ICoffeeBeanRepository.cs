namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface ICoffeeBeanRepository
{
    Task<CoffeeBean?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CoffeeBean?> GetByNameAsync(string name, CancellationToken ct = default);
    void Add(CoffeeBean bean);
    void Remove(CoffeeBean bean);
}
