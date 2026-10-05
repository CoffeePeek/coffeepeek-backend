using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Account.Persistence.PublicAddresses;

/// <summary>
/// Visibility-filtered profile address lookup and the bounded admin backfill (single-kind reduction of the Shops
/// PublicAddressService). No admin initialize, rename or history exists for users (D-06, D-10).
/// </summary>
public sealed class ProfileAddressService(AccountDbContext db) : IProfileAddressService
{
    // D-08: canonical paths are built only from this literal plus the stored, validated slug, never from request input.
    private const string CanonicalPrefix = "/users/";

    public async Task<PublicAddressMetadata?> ResolveAsync(string slug, CancellationToken ct)
    {
        RequireEnabled();
        var normalized = slug.ToLowerInvariant();
        if (!PublicSlug.IsValid(normalized)) throw new ValidationException("Invalid public address.");

        return await db.Users.AsNoTracking().Where(u =>
            u.PublicSlug == normalized && !u.IsSoftDelete && !u.IsBlocked)
            .Select(u => new PublicAddressMetadata(u.Id, u.PublicSlug!, CanonicalPrefix + u.PublicSlug, u.SlugRevision, false))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<PublicAddressMetadata[]> GetByIdsAsync(Guid[] ids, CancellationToken ct)
    {
        RequireEnabled();
        if (ids.Length is < 1 or > 100 || ids.Contains(Guid.Empty))
            throw new ValidationException("Supply 1–100 nonempty user IDs.");

        var distinct = ids.Distinct().ToArray();
        var addresses = await db.Users.AsNoTracking().Where(u => distinct.Contains(u.Id) &&
            !u.IsSoftDelete && !u.IsBlocked && u.PublicSlug != null)
            .Select(u => new PublicAddressMetadata(u.Id, u.PublicSlug!, CanonicalPrefix + u.PublicSlug, u.SlugRevision, false))
            .ToArrayAsync(ct);
        return addresses.OrderBy(m => Array.IndexOf(distinct, m.EntityId)).ToArray();
    }

    public async Task<PublicAddressBackfillResult> BackfillAsync(Guid? afterId, int limit, bool dryRun, CancellationToken ct)
    {
        RequireEnabled();
        if (limit is < 1 or > 100) throw new ValidationException("Limit must be 1–100.");

        async Task<PublicAddressBackfillResult> Execute()
        {
            // Blocked users are included: they are hidden by visibility, and unblocking must not need a new allocation.
            // The converted Username is projected whole and read client-side (u.Username.Value is not translatable).
            var owners = await db.Users
                .Where(u => !u.IsSoftDelete && (!afterId.HasValue || u.Id.CompareTo(afterId.Value) > 0))
                .OrderBy(u => u.Id)
                .Take(limit + 1)
                .ToArrayAsync(ct);

            var proposals = new HashSet<string>(StringComparer.Ordinal);
            var items = new List<PublicAddressBackfillItem>();
            foreach (var owner in owners.Take(limit))
            {
                var username = owner.Username.Value;
                var initialized = owner.PublicSlug is not null;
                var stem = ProfileAddressWriter.Stem(username);
                var slug = owner.PublicSlug ?? await ProfileAddressWriter.FindCandidateAsync(db, username, owner.Id, proposals, ct);
                proposals.Add(slug);
                if (!dryRun && !initialized) owner.SetPublicSlug(slug);
                items.Add(new PublicAddressBackfillItem(owner.Id, slug,
                    slug != PublicSlug.Candidate(stem.Name, owner.Id, stem.FirstAttempt), initialized));
            }

            // Only slug fields changed; the SaveChanges hook leaves usernames and their slugs as prepared above.
            if (!dryRun) await db.SaveChangesAsync(ct);
            return new PublicAddressBackfillResult(dryRun, items.ToArray(), owners.Length > limit ? owners[limit - 1].Id : null);
        }

        try
        {
            return dryRun ? await Execute() : await ProfileAddressTransaction.RunAsync(db, Execute, ct);
        }
        catch (DbUpdateException ex) when (IsAddressConflict(ex))
        {
            // A concurrent allocation took the slug: 409 instead of 500 (live PostgreSQL gate item).
            throw new ConflictException("Address is reserved or changed concurrently.", ex);
        }
    }

    // D-03: a profile is public iff it is not soft-deleted and not blocked. Email confirmation is deliberately not
    // required. This is a live read with no cache.
    private Task<bool> IsVisibleAsync(Guid id, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == id && !u.IsSoftDelete && !u.IsBlocked, ct);

    private static PublicAddressMetadata Metadata(User row) =>
        new(row.Id, row.PublicSlug!, CanonicalPrefix + row.PublicSlug, row.SlugRevision);

    private void RequireEnabled()
    {
        if (!db.PublicAddressesEnabled) throw new ServiceUnavailableException("Public addresses are not enabled.");
    }

    private static bool IsAddressConflict(DbUpdateException ex) => ex.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } constraint
    } && constraint == "IX_Users_Slug";
}
