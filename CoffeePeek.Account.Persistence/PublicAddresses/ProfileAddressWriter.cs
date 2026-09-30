using CoffeePeek.Account.Domain.Entities.PublicAddressAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CoffeePeek.Account.Persistence.PublicAddresses;

/// <summary>
/// SaveChanges hook branches for user profile addresses: retire on deletion (always, D-03), allocate on creation and
/// follow username changes (Policy A, D-01) while enabled. Every lookup checks the tracked Local view first.
/// </summary>
internal static class ProfileAddressWriter
{
    private const int MaxAttempts = 6;
    private const string FallbackStem = "user";

    public static bool IsRetiring(EntityEntry<User> e) =>
        e.State == EntityState.Deleted ||
        (e.State == EntityState.Modified && e.Entity.IsSoftDelete && e.Property(u => u.IsSoftDelete).OriginalValue == false);

    // Compare original with current values and never use IsModified: UserRepository.Update (DbSet.Update) marks every
    // property modified (research Pitfall 1). The soft-delete guard also covers a future anonymization of deleted users.
    public static bool NeedsAddress(EntityEntry<User> e) =>
        !e.Entity.IsSoftDelete &&
        (e.State == EntityState.Added ||
         (e.State == EntityState.Modified && e.Property(u => u.Username).OriginalValue != e.Entity.Username));

    // No person owns a bare "place" or "user" address: reserved, unsupported-script and "user" usernames always get
    // "user-<8hex>". 04-RESEARCH.md Pattern 3 (Claude's discretion); this intentionally differs from the spec's generic
    // "place" fallback and is documented in plan 04-03.
    public static (string Name, int FirstAttempt) Stem(string username) =>
        PublicSlug.Generate(username) is "place" or FallbackStem ? (FallbackStem, 1) : (username, 0);

    // Owner-aware (Pattern 3): a slug already owned by this user (own canonical or own alias) is not a conflict, so a
    // suffixed owner's case-only change never mints a longer suffix. Another owner's row or a tombstone is taken.
    public static async Task<(string Slug, UserPublicAddress? Row)> FindCandidateAsync(AccountDbContext db,
        string username, Guid ownerId, ISet<string>? proposed, CancellationToken ct)
    {
        var stem = Stem(username);
        for (var attempt = stem.FirstAttempt; attempt < MaxAttempts; attempt++)
        {
            var slug = PublicSlug.Candidate(stem.Name, ownerId, attempt);
            if (proposed?.Contains(slug) == true) continue;
            var row = db.UserPublicAddresses.Local.FirstOrDefault(a => a.Slug == slug)
                ?? await db.UserPublicAddresses.SingleOrDefaultAsync(a => a.Slug == slug, ct);
            if (row is null || row.OwnerId == ownerId) return (slug, row);
        }
        throw new ConflictException("Could not allocate a unique profile address.");
    }

    public static async Task<int> SaveAsync(AccountDbContext db, EntityEntry<User>[] users,
        bool acceptAllChangesOnSuccess, CancellationToken ct)
    {
        // Classify synchronously, before any await: the demotion flush accepts changes and resets entry states,
        // so entries must not be re-inspected afterwards.
        var retiring = users.Where(IsRetiring).Select(e => e.Entity.Id).ToArray();
        var created = users.Where(e => e.State == EntityState.Added && NeedsAddress(e))
            .Select(e => (e.Entity.Id, e.Entity.Username.Value)).ToArray();
        var renamed = users.Where(e => e.State == EntityState.Modified && NeedsAddress(e))
            .Select(e => (e.Entity.Id, e.Entity.Username.Value)).ToArray();

        // Always, even when disabled: retirement is a privacy rule and releases the RESTRICT FK (D-03).
        foreach (var ownerId in retiring) await RetireAllAsync(db, ownerId, ct);
        if (!db.PublicAddressesEnabled) return await db.SaveCoreAsync(acceptAllChangesOnSuccess, ct);

        foreach (var (ownerId, username) in created) await InitializeAsync(db, ownerId, username, ct);
        var affected = 0;
        foreach (var (ownerId, username) in renamed) affected += await FollowUsernameAsync(db, ownerId, username, ct);
        return affected + await db.SaveCoreAsync(acceptAllChangesOnSuccess, ct);
    }

    private static async Task RetireAllAsync(AccountDbContext db, Guid ownerId, CancellationToken ct)
    {
        var persisted = await db.UserPublicAddresses.Where(a => a.OwnerId == ownerId).ToListAsync(ct);
        var rows = persisted.Concat(db.UserPublicAddresses.Local.Where(a => a.OwnerId == ownerId))
            .DistinctBy(a => a.Id).ToArray();
        if (rows.Length == 0) return;
        var revision = checked(rows.Max(a => a.Revision) + 1);
        var now = DateTime.UtcNow;
        // No audit row (D-06): a tombstone keeps only the reserved slug and never the owner.
        foreach (var row in rows) row.Retire(revision, now);
    }

    private static async Task InitializeAsync(AccountDbContext db, Guid ownerId, string username, CancellationToken ct)
    {
        if (await FindCanonicalAsync(db, ownerId, ct) is not null) return;
        // An owner without a canonical row never owns aliases, so the candidate row is always null here.
        var (slug, _) = await FindCandidateAsync(db, username, ownerId, null, ct);
        db.UserPublicAddresses.Add(UserPublicAddress.Create(ownerId, slug, 1, DateTime.UtcNow));
    }

    private static async Task<int> FollowUsernameAsync(AccountDbContext db, Guid ownerId, string username, CancellationToken ct)
    {
        var current = await FindCanonicalAsync(db, ownerId, ct);
        if (current is null)
        {
            // A pre-backfill user (created while disabled) is allocated on the first username change.
            await InitializeAsync(db, ownerId, username, ct);
            return 0;
        }

        var (slug, row) = await FindCandidateAsync(db, username, ownerId, null, ct);
        // Case- or punctuation-only change, including the owner's own suffixed slug: nothing to rename (D-01).
        if (slug == current.Slug) return 0;

        var revision = checked(current.Revision + 1);
        var now = DateTime.UtcNow;
        current.MakeAlias(revision, now);
        // The demotion must reach the database before the promotion because of the filtered unique owner index
        // (as in phase 2 ChangeAsync). Never call db.SaveChangesAsync here: it would re-enter the hook (Pitfall 2).
        var flushed = await db.SaveCoreAsync(true, ct);
        if (row is not null) row.Restore(revision, now);
        else db.UserPublicAddresses.Add(UserPublicAddress.Create(ownerId, slug, revision, now));
        return flushed;
    }

    private static async Task<UserPublicAddress?> FindCanonicalAsync(AccountDbContext db, Guid ownerId, CancellationToken ct)
    {
        var local = db.UserPublicAddresses.Local.FirstOrDefault(a => a.OwnerId == ownerId && a.State == PublicAddressState.Canonical);
        if (local is not null) return local;
        var persisted = await db.UserPublicAddresses.SingleOrDefaultAsync(
            a => a.OwnerId == ownerId && a.State == PublicAddressState.Canonical, ct);
        // Identity resolution may return a tracked row demoted or retired in this save but not yet flushed.
        return persisted is { State: PublicAddressState.Canonical } ? persisted : null;
    }
}
