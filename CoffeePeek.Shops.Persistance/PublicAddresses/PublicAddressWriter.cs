using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CoffeePeek.Shops.Persistance.PublicAddresses;

internal static class PublicAddressWriter
{
    public static async Task PrepareOwnerChangesAsync(ShopsDbContext db, CancellationToken ct)
    {
        if (!db.PublicAddressesEnabled) return;
        foreach (var entry in db.ChangeTracker.Entries<CoffeeShop>().Where(e => e.State == EntityState.Added).ToArray())
            await InitializeAsync(db, entry.Entity, Stem(entry.Entity), ct);
        foreach (var entry in db.ChangeTracker.Entries<Roaster>().Where(e => e.State == EntityState.Added).ToArray())
            await InitializeAsync(db, entry.Entity, Stem(entry.Entity), ct);
        foreach (var entry in db.ChangeTracker.Entries<City>().Where(e => e.State == EntityState.Added).ToArray())
            await InitializeAsync(db, entry.Entity, Stem(entry.Entity), ct);
        foreach (var entry in db.ChangeTracker.Entries<CoffeeBean>().Where(e => e.State == EntityState.Added).ToArray())
            await InitializeAsync(db, entry.Entity, Stem(entry.Entity), ct);
        foreach (var entry in db.ChangeTracker.Entries<Equipment>().Where(e => e.State == EntityState.Added).ToArray())
            await InitializeAsync(db, entry.Entity, Stem(entry.Entity), ct);
        foreach (var entry in db.ChangeTracker.Entries<BrewMethod>().Where(e => e.State == EntityState.Added).ToArray())
            await InitializeAsync(db, entry.Entity, Stem(entry.Entity), ct);
        foreach (var entry in db.ChangeTracker.Entries<ShopTag>().Where(e => e.State == EntityState.Added).ToArray())
            await InitializeAsync(db, entry.Entity, Stem(entry.Entity), ct);
        foreach (var entry in db.ChangeTracker.Entries<CoffeeZone>().Where(IsNewlyPublished).ToArray())
            await InitializeAsync(db, entry.Entity, entry.Entity.Name, ct);
    }

    internal static bool IsNewlyPublished(EntityEntry entry) =>
        entry.Entity is CoffeeZone { Status: CoffeeZoneStatus.Published } &&
        (entry.State == EntityState.Added || (entry.State == EntityState.Modified &&
            entry.Property(nameof(CoffeeZone.Status)).IsModified));

    public static string ShopStem(CoffeeShop shop) => string.IsNullOrWhiteSpace(shop.Location?.Address)
        ? shop.Name : $"{shop.Name} {shop.Location.Address}";

    public static string Stem(IPublicSlugOwner owner) => owner switch
    {
        CoffeeShop s => ShopStem(s), Roaster s => s.Name, City s => s.Name, CoffeeZone s => s.Name,
        CoffeeBean s => s.Name, Equipment s => s.Name, BrewMethod s => s.Name, ShopTag s => s.Slug,
        _ => throw new InvalidOperationException("Unknown public slug owner.")
    };

    public static async Task InitializeAsync<T>(ShopsDbContext db, T owner, string stem, CancellationToken ct)
        where T : class, IPublicSlugOwner
    {
        if (owner.PublicSlug is not null) return;
        owner.SetPublicSlug(await FindCandidateAsync<T>(db, stem, owner.Id, null, ct));
    }

    public static async Task<string> FindCandidateAsync<T>(ShopsDbContext db, string stem, Guid ownerId,
        ISet<string>? proposed, CancellationToken ct) where T : class, IPublicSlugOwner
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var slug = PublicSlug.Candidate(stem, ownerId, attempt);
            if (proposed?.Contains(slug) == true || db.Set<T>().Local.Any(a => a.Id != ownerId && a.PublicSlug == slug)) continue;
            if (!await db.Set<T>().AnyAsync(a => a.Id != ownerId && a.PublicSlug == slug, ct)) return slug;
        }
        throw new PublicAddressException("Could not allocate a unique slug. Choose a manual slug.", 409);
    }
}
