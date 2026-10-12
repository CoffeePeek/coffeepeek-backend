using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

public sealed class CoffeeCatalogTagStorage(ShopsDbContext db) : ICoffeeCatalogTagStorage
{
    public Task<bool> IsSlugReservedAsync(string slug, CancellationToken ct) => db.RoasterTags.AnyAsync(t => t.Slug == slug, ct);
    public async Task<RoasterTag?> FindAsync(Guid id, CancellationToken ct) => await db.RoasterTags.FindAsync([id], ct);
    public Task<bool> RoasterExistsAsync(Guid id, CancellationToken ct) => db.Roasters.AnyAsync(r => r.Id == id, ct);
    public Task<int> CountActiveAsync(Guid[] ids, CancellationToken ct) => db.RoasterTags.CountAsync(t => ids.Contains(t.Id) && t.IsActive, ct);
    public Task<RoasterTagAssignment[]> AssignmentsAsync(Guid roasterId, CancellationToken ct) =>
        db.RoasterTagAssignments.Where(a => a.RoasterId == roasterId).ToArrayAsync(ct);
    public async Task SaveTagAsync(RoasterTag tag, bool add, CancellationToken ct)
    {
        if (add) db.RoasterTags.Add(tag);
        await db.SaveChangesAsync(ct);
    }
    public async Task SaveAssignmentsAsync(RoasterTagAssignment[] added, RoasterTagAssignment[] removed, CancellationToken ct)
    {
        db.RoasterTagAssignments.RemoveRange(removed); db.RoasterTagAssignments.AddRange(added);
        await db.SaveChangesAsync(ct);
    }
}
