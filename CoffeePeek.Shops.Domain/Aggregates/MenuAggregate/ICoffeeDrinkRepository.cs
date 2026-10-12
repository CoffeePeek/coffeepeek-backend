namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public interface ICoffeeDrinkRepository : IQueryCoffeeDrinkRepository
{
    Task<CoffeeDrinkDefinition[]> GetAllAsync(CancellationToken ct);
    Task<CoffeeDrinkDefinition?> FindTrackedAsync(string slug, CancellationToken ct);
    void Add(CoffeeDrinkDefinition drink);
}
