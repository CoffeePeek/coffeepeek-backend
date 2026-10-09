using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface ICoffeeProductStorage
{
    Task<bool> RoasterExistsAsync(Guid roasterId, CancellationToken ct);
    Task<string[]> ExistingCountriesAsync(string[] codes, CancellationToken ct);
    Task<CoffeeProduct?> FindAsync(Guid id, CancellationToken ct);
    void Add(CoffeeProduct product);
    Task FlushAsync(CancellationToken ct);
}
