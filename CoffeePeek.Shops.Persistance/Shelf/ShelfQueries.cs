using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Shelf;

public sealed class ShelfQueries(ShopsDbContext db, IShelfCatalogLookup catalog) : IShelfQueries
{
    public async Task<ShelfBagDto[]> GetShelfAsync(Guid userId, CancellationToken ct)
    {
        var bags = await db.ShelfBags.AsNoTracking()
            .Where(b => b.UserId == userId && b.Status == ShelfBagStatus.OnShelf)
            .OrderBy(b => b.Position).ThenBy(b => b.AddedAtUtc).ThenBy(b => b.Id).ToArrayAsync(ct);
        return await ShelfBagMapping.ToDtosAsync(bags, catalog, ct);
    }

    public async Task<ShelfArchivePage> GetArchiveAsync(Guid userId, int pageNumber, int pageSize, string? query,
        CancellationToken ct)
    {
        var archive = db.ShelfBags.AsNoTracking().Where(b => b.UserId == userId && b.Status == ShelfBagStatus.Finished);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = "%" + query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            archive = archive.Where(b => EF.Functions.ILike(b.Name ?? "", pattern, "\\")
                || EF.Functions.ILike(b.RoasterName ?? "", pattern, "\\"));
        }
        var total = await archive.CountAsync(ct);
        var bags = await archive.OrderByDescending(b => b.FinishedAtUtc).ThenBy(b => b.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new ShelfArchivePage(await ShelfBagMapping.ToDtosAsync(bags, catalog, ct), total);
    }

    public Task<ShelfBagPhotoAccess?> GetPhotoAsync(Guid userId, Guid bagId, Guid photoId, CancellationToken ct) =>
        db.ShelfBags.AsNoTracking()
            .Where(b => b.Id == bagId && b.UserId == userId && b.PhotoId == photoId)
            .Select(b => new ShelfBagPhotoAccess(b.UserId, b.Id, photoId, b.PhotoContentType!))
            .SingleOrDefaultAsync(ct);
}
