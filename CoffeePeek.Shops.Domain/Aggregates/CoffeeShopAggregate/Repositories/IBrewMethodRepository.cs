using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IBrewMethodRepository
{
    Task<BrewMethod?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<BrewMethod?> GetByNameAsync(string name, CancellationToken ct = default);
    void Add(BrewMethod brewMethod);
    void Remove(BrewMethod brewMethod);
}
