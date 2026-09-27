using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.PublicAddresses;

internal static class PublicAddressWriter
{
    public static async Task PrepareOwnerChangesAsync(ShopsDbContext db, CancellationToken ct)
    {
        var shops = db.ChangeTracker.Entries<CoffeeShop>().ToArray();
        var roasters = db.ChangeTracker.Entries<Roaster>().ToArray();
        foreach (var entry in shops.Where(e => e.State == EntityState.Deleted))
            await RetireAsync<CoffeeShopPublicAddress>(db, PublicAddressKind.CoffeeShop, entry.Entity.Id, ct);
        foreach (var entry in roasters.Where(e => e.State == EntityState.Deleted))
            await RetireAsync<RoasterPublicAddress>(db, PublicAddressKind.Roaster, entry.Entity.Id, ct);
        if (!db.PublicAddressesEnabled) return;
        foreach (var entry in shops.Where(e => e.State == EntityState.Added))
            await InitializeAsync<CoffeeShopPublicAddress>(db, PublicAddressKind.CoffeeShop, entry.Entity.Id,
                ShopStem(entry.Entity), null, "Automatically allocated on creation", ct);
        foreach (var entry in roasters.Where(e => e.State == EntityState.Added))
            await InitializeAsync<RoasterPublicAddress>(db, PublicAddressKind.Roaster, entry.Entity.Id,
                entry.Entity.Name, null, "Automatically allocated on creation", ct);
    }

    public static string ShopStem(CoffeeShop shop) => string.IsNullOrWhiteSpace(shop.Location?.Address)
        ? shop.Name : $"{shop.Name} {shop.Location.Address}";

    public static async Task<T> InitializeAsync<T>(ShopsDbContext db, PublicAddressKind kind, Guid ownerId,
        string stem, Guid? actorId, string reason, CancellationToken ct) where T : PublicAddress
    {
        var existing = db.Set<T>().Local.FirstOrDefault(a => a.OwnerId == ownerId && a.State == PublicAddressState.Canonical)
            ?? await db.Set<T>().SingleOrDefaultAsync(a => a.OwnerId == ownerId && a.State == PublicAddressState.Canonical, ct);
        if (existing is not null) return existing;
        var slug = await FindCandidateAsync<T>(db, stem, ownerId, null, ct);
        var row = Create<T>(ownerId, slug, 1, DateTime.UtcNow);
        db.Set<T>().Add(row);
        Audit(db, kind, ownerId, 1, null, slug, "initialize", actorId, reason, row.CreatedAtUtc);
        return row;
    }

    public static async Task<string> FindCandidateAsync<T>(ShopsDbContext db, string stem, Guid ownerId,
        ISet<string>? proposed, CancellationToken ct) where T : PublicAddress
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var slug = PublicSlug.Candidate(stem, ownerId, attempt);
            if (proposed?.Contains(slug) == true || db.Set<T>().Local.Any(a => a.Slug == slug)) continue;
            if (!await db.Set<T>().AnyAsync(a => a.Slug == slug, ct)) return slug;
        }
        throw new PublicAddressException("Could not allocate a unique public address. Choose a manual slug.", 409);
    }

    public static T Create<T>(Guid ownerId, string slug, long revision, DateTime now) where T : PublicAddress =>
        (T)(PublicAddress)(typeof(T) == typeof(CoffeeShopPublicAddress)
            ? CoffeeShopPublicAddress.Create(ownerId, slug, revision, now)
            : RoasterPublicAddress.Create(ownerId, slug, revision, now));

    private static async Task RetireAsync<T>(ShopsDbContext db, PublicAddressKind kind, Guid ownerId, CancellationToken ct) where T : PublicAddress
    {
        var persisted = await db.Set<T>().Where(a => a.OwnerId == ownerId).ToListAsync(ct);
        var rows = persisted.Concat(db.Set<T>().Local.Where(a => a.OwnerId == ownerId)).DistinctBy(a => a.Id).ToArray();
        if (rows.Length == 0) return;
        var revision = checked(rows.Max(a => a.Revision) + 1);
        var now = DateTime.UtcNow;
        var previous = rows.SingleOrDefault(a => a.State == PublicAddressState.Canonical)?.Slug;
        foreach (var row in rows) row.Retire(revision, now);
        Audit(db, kind, ownerId, revision, previous, null, "retire", null, "Owner deleted", now);
    }

    public static void Audit(ShopsDbContext db, PublicAddressKind kind, Guid ownerId, long revision,
        string? previous, string? slug, string action, Guid? actorId, string reason, DateTime now) =>
        db.PublicAddressAudit.Add(new PublicAddressAudit
        {
            Kind = kind, OwnerId = ownerId, Revision = revision, PreviousSlug = previous, Slug = slug,
            Action = action, ActorId = actorId, Reason = reason, AtUtc = now
        });
}
