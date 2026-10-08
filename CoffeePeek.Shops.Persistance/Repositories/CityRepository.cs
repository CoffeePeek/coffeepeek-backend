using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public sealed class CityRepository(ShopsDbContext dbContext) : ICityRepository
{
    public Task<City?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Cities.FirstOrDefaultAsync(c => c.Id == id, ct);

    public void Add(City city) => dbContext.Cities.Add(city);

    public async Task<CityDeleteResult> DeleteIfUnusedAsync(Guid id, CancellationToken ct = default)
    {
        var city = await GetByIdAsync(id, ct);
        if (city is null)
            return CityDeleteResult.NotFound;

        // The precheck gives a useful response without relying on a database error.
        // Foreign keys remain the final arbiter if a reference is inserted concurrently.
        if (await dbContext.Shops.AnyAsync(s => s.Location.CityId == id, ct)
            || await dbContext.Roasters.AnyAsync(r => r.Location != null && r.Location.CityId == id, ct)
            || await dbContext.CoffeeZones.AnyAsync(z => z.CityId == id, ct))
            return CityDeleteResult.Referenced;

        dbContext.Cities.Remove(city);
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return CityDeleteResult.Deleted;
        }
        catch (DbUpdateException ex) when (CityReferenceViolation.Matches(ex))
        {
            dbContext.Entry(city).State = EntityState.Unchanged;
            return CityDeleteResult.Referenced;
        }
    }
}
