using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Shelf;

public sealed class ShelfBagRepository(ShopsDbContext db) : IShelfBagRepository
{
    public Task LockOwnerAsync(Guid userId, CancellationToken ct = default) =>
        // Transaction-scoped: released on commit/rollback of the Wolverine handler transaction.
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"shelf:" + userId}, 0))", ct);

    public Task<ShelfBag?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.ShelfBags.SingleOrDefaultAsync(b => b.Id == id, ct);

    public Task<List<ShelfBag>> GetOnShelfAsync(Guid userId, CancellationToken ct = default) =>
        db.ShelfBags.Where(b => b.UserId == userId && b.Status == ShelfBagStatus.OnShelf)
            .OrderBy(b => b.Position).ThenBy(b => b.AddedAtUtc).ThenBy(b => b.Id).ToListAsync(ct);

    public Task<List<ShelfBag>> GetAllAsync(Guid userId, CancellationToken ct = default) =>
        db.ShelfBags.Where(b => b.UserId == userId).ToListAsync(ct);

    public void Add(ShelfBag bag) => db.ShelfBags.Add(bag);

    public void Remove(ShelfBag bag) => db.ShelfBags.Remove(bag);
}
