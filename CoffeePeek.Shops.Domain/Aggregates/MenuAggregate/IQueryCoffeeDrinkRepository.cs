namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public interface IQueryCoffeeDrinkRepository
{
    Task<CoffeeDrinkDefinition[]> GetActiveAsync(CancellationToken ct = default);
}
