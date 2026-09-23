namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IRoasterRepository
{
    Task<Roaster?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Roaster?> GetByNameAsync(string name, CancellationToken ct = default);
    void Add(Roaster roaster);
    void Remove(Roaster roaster);
}
