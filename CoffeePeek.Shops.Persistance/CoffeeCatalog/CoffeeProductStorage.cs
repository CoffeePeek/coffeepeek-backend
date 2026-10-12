using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed class CoffeeProductStorage(ShopsDbContext db) : ICoffeeProductStorage
{
    public Task<bool> RoasterExistsAsync(Guid id, CancellationToken ct) => db.Roasters.AnyAsync(r => r.Id == id, ct);
    public Task<string[]> ExistingCountriesAsync(string[] codes, CancellationToken ct) =>
        db.Countries.Where(c => codes.Contains(c.Code)).Select(c => c.Code).ToArrayAsync(ct);
    public Task<CoffeeProduct?> FindAsync(Guid id, CancellationToken ct) =>
        db.CoffeeProducts.Include(p => p.Countries).SingleOrDefaultAsync(p => p.Id == id, ct);
    public void Add(CoffeeProduct product) => db.CoffeeProducts.Add(product);
    public async Task FlushAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new CoffeeCatalogException("Coffee changed; reload before saving.", 409); }
    }
}
