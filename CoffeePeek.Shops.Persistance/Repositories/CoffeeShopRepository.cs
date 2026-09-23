using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class CoffeeShopRepository(ShopsDbContext dbContext) : ICoffeeShopRepository
{
    private const string CoffeeShopIdShadow = "CoffeeShopId";

    public Task<CoffeeShop?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return QueryForMutation(dbContext.Shops)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public Task<CoffeeShop?> GetByIdForOwnerAsync(Guid id, Guid ownerUserId, CancellationToken ct = default)
    {
        return QueryForMutation(dbContext.Shops)
            .FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == ownerUserId, ct);
    }

    public async Task<CoffeeShop?> GetByIdWithCatalogsAsync(Guid id, CancellationToken ct = default)
    {
        var shop = await GetByIdAsync(id, ct);
        if (shop is null)
            return null;

        await LoadCatalogsAsync(shop, ct);
        return shop;
    }

    public async Task<CoffeeShop?> GetByIdWithCatalogsForOwnerAsync(
        Guid id,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        var shop = await GetByIdForOwnerAsync(id, ownerUserId, ct);
        if (shop is null)
            return null;

        await LoadCatalogsAsync(shop, ct);
        return shop;
    }

    public async Task<bool> TryAttachGalleryPhotosAsync(
        Guid shopId,
        Guid? ownerUserId,
        IReadOnlyList<ShopPhoto> photos,
        CancellationToken ct = default)
    {
        var existsQuery = dbContext.Shops.AsNoTracking().Where(s => s.Id == shopId);
        if (ownerUserId is Guid owner)
            existsQuery = existsQuery.Where(s => s.OwnerUserId == owner);

        if (!await existsQuery.AnyAsync(ct))
            return false;

        var maxSort = await dbContext.ShopPhotos
            .Where(p => EF.Property<Guid?>(p, CoffeeShopIdShadow) == shopId)
            .Select(p => (int?)p.SortIndex)
            .MaxAsync(ct) ?? -1;

        var nextIndex = maxSort + 1;
        foreach (var photo in photos)
        {
            photo.SetSortIndex(nextIndex++);
            dbContext.ShopPhotos.Add(photo);
            dbContext.Entry(photo).Property(CoffeeShopIdShadow).CurrentValue = shopId;
        }

        return true;
    }

    /// <summary>
    /// Tracked load for writes. Photos + tags only — no AsSplitQuery and no sibling
    /// catalog Includes. Loading owned schedules together with many collections via
    /// split queries made SaveChanges issue an UPDATE that hit 0 rows
    /// (DbUpdateConcurrencyException) when attaching gallery photos.
    /// </summary>
    private static IQueryable<CoffeeShop> QueryForMutation(IQueryable<CoffeeShop> shops) =>
        shops
            .Include(s => s.ShopPhotos)
            .Include(s => s.ShopTags);

    private async Task LoadCatalogsAsync(CoffeeShop shop, CancellationToken ct)
    {
        var entry = dbContext.Entry(shop);
        await entry.Collection(s => s.Equipments).LoadAsync(ct);
        await entry.Collection(s => s.CoffeeBeans).LoadAsync(ct);
        await entry.Collection(s => s.Roasters).LoadAsync(ct);
        await entry.Collection(s => s.BrewMethods).LoadAsync(ct);
    }
}

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
        var items = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<CoffeeShop>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        return await dbContext.Shops
            .AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }
}
