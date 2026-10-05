using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CoffeePeek.Account.Persistence.PublicAddresses;

internal static class ProfileAddressWriter
{
    public static bool NeedsClear(EntityEntry<User> e) => e.State == EntityState.Modified &&
        e.Entity.IsSoftDelete && e.Entity.PublicSlug is not null;

    public static bool NeedsAddress(EntityEntry<User> e) => !e.Entity.IsSoftDelete &&
        (e.State == EntityState.Added || (e.State == EntityState.Modified &&
            e.Property(u => u.Username).OriginalValue != e.Entity.Username));

    public static (string Name, int FirstAttempt) Stem(string username) =>
        PublicSlug.Generate(username) is "place" or "user" ? ("user", 1) : (username, 0);

    public static async Task<string> FindCandidateAsync(AccountDbContext db, string username,
        Guid ownerId, ISet<string>? proposed, CancellationToken ct)
    {
        var stem = Stem(username);
        for (var attempt = stem.FirstAttempt; attempt < 6; attempt++)
        {
            var slug = PublicSlug.Candidate(stem.Name, ownerId, attempt);
            if (proposed?.Contains(slug) == true || db.Users.Local.Any(u => u.Id != ownerId && u.PublicSlug == slug)) continue;
            if (!await db.Users.AnyAsync(u => u.Id != ownerId && u.PublicSlug == slug, ct)) return slug;
        }
        throw new ConflictException("Could not allocate a unique profile slug.");
    }

    public static async Task<int> SaveAsync(AccountDbContext db, EntityEntry<User>[] users,
        bool acceptAllChangesOnSuccess, CancellationToken ct)
    {
        foreach (var entry in users.Where(NeedsClear)) entry.Entity.ClearPublicSlug();
        if (db.PublicAddressesEnabled)
            foreach (var entry in users.Where(NeedsAddress))
                entry.Entity.SetPublicSlug(await FindCandidateAsync(db, entry.Entity.Username.Value, entry.Entity.Id, null, ct));
        return await db.SaveCoreAsync(acceptAllChangesOnSuccess, ct);
    }
}
