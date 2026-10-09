using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class AdminCoffeeShopQueryRepository(ShopsDbContext dbContext) : IAdminCoffeeShopQueryRepository
{
    public async Task<(IReadOnlyList<CoffeeShop> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        CoffeeShopStatus? status,
        CancellationToken ct = default,
        bool? importedFromFile = null)
    {
        var query = dbContext.Shops.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                s.Location.Address.ToLower().Contains(term));
        }

        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        if (importedFromFile == true)
            query = query.Where(s => s.ImportedFromFileAt != null);
        else if (importedFromFile == false)
            query = query.Where(s => s.ImportedFromFileAt == null);

        var totalCount = await query.CountAsync(ct);
        var items = await WithAdminCollections(query)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<CoffeeShop>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        return await WithAdminCollections(dbContext.Shops
                .AsNoTracking()
                .Where(s => s.OwnerUserId == ownerUserId))
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);
    }

    private static IQueryable<CoffeeShop> WithAdminCollections(IQueryable<CoffeeShop> query) =>
        query
            .Include(s => s.ShopPhotos)
            .Include(s => s.ShopTags)
            .Include(s => s.ShopEquipments).ThenInclude(a => a.Equipment)
            .Include(s => s.CoffeeBeans)
            .Include(s => s.Roasters)
            .Include(s => s.BrewMethods)
            // A single join across these sibling collections would multiply rows and can
            // turn a small page into a very large SQL result. Split queries stay bounded
            // by the number of collections, not by the number of shops on the page.
            .AsSplitQuery();
}
