namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IQueryCityRepository
{
    Task<City[]> GetAll(CancellationToken ct = default);
    Task<bool> Exists(Guid cityId, CancellationToken ct = default);
    Task<City?> GetByName(string name, CancellationToken ct = default);
    Task<City?> GetById(Guid id, CancellationToken ct = default);
}