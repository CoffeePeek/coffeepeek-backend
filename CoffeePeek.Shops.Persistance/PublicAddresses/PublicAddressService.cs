using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Persistance.PublicAddresses;

public sealed class PublicAddressService(ShopsDbContext db) : IPublicAddressService
{
    public Task<PublicAddressMetadata?> ResolveAsync(PublicAddressKind kind, string slug, CancellationToken ct)
    {
        RequireEnabled(kind);
        var normalized = slug.ToLowerInvariant();
        if (!PublicSlug.IsValid(normalized)) throw new PublicAddressException("Invalid public address.", 400);
        return kind == PublicAddressKind.CoffeeShop
            ? ResolveAsync<CoffeeShopPublicAddress>(kind, slug, normalized, ct)
            : ResolveAsync<RoasterPublicAddress>(kind, slug, normalized, ct);
    }

    private async Task<PublicAddressMetadata?> ResolveAsync<T>(PublicAddressKind kind, string requested, string normalized, CancellationToken ct) where T : PublicAddress
    {
        var address = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(a => a.Slug == normalized, ct);
        if (address?.OwnerId is not { } ownerId) return null;
        // Never trust a cached detail payload to determine current public visibility.
        if (!await IsVisibleAsync(kind, ownerId, ct)) return null;
        var current = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(a => a.OwnerId == ownerId && a.State == PublicAddressState.Canonical, ct);
        return current is null ? null : Metadata(kind, current, requested != current.Slug);
    }

    public Task<PublicAddressMetadata[]> GetByIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (ids.Length is < 1 or > 100 || ids.Contains(Guid.Empty)) throw new PublicAddressException("Supply 1–100 nonempty entity IDs.", 400);
        return kind == PublicAddressKind.CoffeeShop
            ? GetByIdsAsync<CoffeeShopPublicAddress>(kind, ids.Distinct().ToArray(), ct)
            : GetByIdsAsync<RoasterPublicAddress>(kind, ids.Distinct().ToArray(), ct);
    }

    private async Task<PublicAddressMetadata[]> GetByIdsAsync<T>(PublicAddressKind kind, Guid[] ids, CancellationToken ct) where T : PublicAddress
    {
        var visible = kind == PublicAddressKind.CoffeeShop
            ? await db.Shops.AsNoTracking().Where(s => ids.Contains(s.Id) && !s.IsHidden).Select(s => s.Id).ToArrayAsync(ct)
            : await db.Roasters.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArrayAsync(ct);
        var rows = await db.Set<T>().AsNoTracking().Where(a => a.OwnerId.HasValue && visible.Contains(a.OwnerId.Value) && a.State == PublicAddressState.Canonical).ToArrayAsync(ct);
        return rows.Select(a => Metadata(kind, a)).OrderBy(a => Array.IndexOf(ids, a.EntityId)).ToArray();
    }

    public async Task<PublicAddressMetadata> ChangeAsync(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request, Guid actorId, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (actorId == Guid.Empty || request.ExpectedRevision < 0 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new PublicAddressException("Actor, nonnegative expected revision and reason (1–500 characters) required.", 400);
        if (request.Slug is not null && !PublicSlug.IsValid(request.Slug)) throw new PublicAddressException("Invalid or reserved slug.", 400);
        try
        {
            return await PublicAddressTransaction.RunAsync(db, () => kind == PublicAddressKind.CoffeeShop
                ? ChangeAsync<CoffeeShopPublicAddress>(kind, id, request, actorId, ct)
                : ChangeAsync<RoasterPublicAddress>(kind, id, request, actorId, ct), ct);
        }
        catch (DbUpdateConcurrencyException) { throw new PublicAddressException("Address changed; reload and retry.", 409); }
        catch (DbUpdateException ex) when (IsAddressConflict(ex)) { throw new PublicAddressException("Address is reserved or changed concurrently.", 409); }
    }

    private async Task<PublicAddressMetadata> ChangeAsync<T>(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request, Guid actorId, CancellationToken ct) where T : PublicAddress
    {
        var stem = await GetStemAsync(kind, id, ct) ?? throw new PublicAddressException("Entity not found.", 404);
        var current = await db.Set<T>().SingleOrDefaultAsync(a => a.OwnerId == id && a.State == PublicAddressState.Canonical, ct);
        if (current is not null && request.ExpectedRevision == 0 && (request.Slug is null || request.Slug == current.Slug))
            return Metadata(kind, current); // Safe retry of initialize: never renames an existing address.
        if ((current?.Revision ?? 0) != request.ExpectedRevision) throw new PublicAddressException("Expected revision does not match the current address.", 409);
        if (current is not null && request.Slug is null) throw new PublicAddressException("A slug is required for renaming.", 400);
        if (current is not null && current.Slug == request.Slug) return Metadata(kind, current);
        var slug = request.Slug ?? await PublicAddressWriter.FindCandidateAsync<T>(db, stem, id, null, ct);
        var target = await db.Set<T>().SingleOrDefaultAsync(a => a.Slug == slug, ct);
        if (target is not null && (target.OwnerId != id || target.State != PublicAddressState.Alias))
            throw new PublicAddressException("This address is permanently reserved.", 409);
        var now = DateTime.UtcNow;
        var revision = checked((current?.Revision ?? 0) + 1);
        if (current is not null)
        {
            current.MakeAlias(revision, now);
            // Flush demotion before promotion; both writes remain in the enclosing transaction/savepoint.
            await db.SaveChangesAsync(ct);
        }
        if (target is not null) target.Restore(revision, now);
        else
        {
            target = PublicAddressWriter.Create<T>(id, slug, revision, now);
            db.Set<T>().Add(target);
        }
        PublicAddressWriter.Audit(db, kind, id, revision, current?.Slug, target.Slug,
            current is null ? "initialize" : "rename", actorId, request.Reason.Trim(), now);
        await db.SaveChangesAsync(ct);
        return Metadata(kind, target);
    }

    public async Task<PublicAddressHistoryItem[]> HistoryAsync(PublicAddressKind kind, Guid id, long afterRevision, int limit, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (afterRevision < 0 || limit is < 1 or > 100) throw new PublicAddressException("Invalid history cursor or limit (1–100).", 400);
        return await db.PublicAddressAudit.AsNoTracking().Where(a => a.Kind == kind && a.OwnerId == id && a.Revision > afterRevision)
            .OrderBy(a => a.Revision).Take(limit)
            .Select(a => new PublicAddressHistoryItem(a.Revision, a.PreviousSlug, a.Slug, a.Action, a.ActorId, a.Reason, a.AtUtc)).ToArrayAsync(ct);
    }

    public Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (limit is < 1 or > 100 || actorId == Guid.Empty) throw new PublicAddressException("Limit must be 1–100 and actor required.", 400);
        Task<PublicAddressBackfillResult> Execute() => kind == PublicAddressKind.CoffeeShop
            ? BackfillAsync<CoffeeShopPublicAddress>(kind, afterId, limit, dryRun, actorId, ct)
            : BackfillAsync<RoasterPublicAddress>(kind, afterId, limit, dryRun, actorId, ct);
        return dryRun ? Execute() : PublicAddressTransaction.RunAsync(db, Execute, ct);
    }

    private async Task<PublicAddressBackfillResult> BackfillAsync<T>(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct) where T : PublicAddress
    {
        var owners = kind == PublicAddressKind.CoffeeShop
            ? await db.Shops.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
                .Select(s => new OwnerStem(s.Id, s.Name, s.Location == null ? null : s.Location.Address)).ToArrayAsync(ct)
            : await db.Roasters.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
                .Select(s => new OwnerStem(s.Id, s.Name, null)).ToArrayAsync(ct);
        var proposals = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<PublicAddressBackfillItem>();
        foreach (var owner in owners.Take(limit))
        {
            var current = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(a => a.OwnerId == owner.Id && a.State == PublicAddressState.Canonical, ct);
            var stem = string.IsNullOrWhiteSpace(owner.Address) ? owner.Name : $"{owner.Name} {owner.Address}";
            var slug = current?.Slug ?? await PublicAddressWriter.FindCandidateAsync<T>(db, stem, owner.Id, proposals, ct);
            proposals.Add(slug);
            if (!dryRun && current is null)
            {
                var row = PublicAddressWriter.Create<T>(owner.Id, slug, 1, DateTime.UtcNow);
                db.Set<T>().Add(row);
                PublicAddressWriter.Audit(db, kind, owner.Id, 1, null, slug, "initialize", actorId, "Backfill", row.CreatedAtUtc);
            }
            items.Add(new PublicAddressBackfillItem(owner.Id, slug, slug != PublicSlug.Generate(stem), current is not null));
        }
        if (!dryRun) await db.SaveChangesAsync(ct);
        return new PublicAddressBackfillResult(dryRun, items.ToArray(), owners.Length > limit ? owners[limit - 1].Id : null);
    }

    private sealed record OwnerStem(Guid Id, string Name, string? Address);
    private async Task<string?> GetStemAsync(PublicAddressKind kind, Guid id, CancellationToken ct)
    {
        if (kind == PublicAddressKind.Roaster) return await db.Roasters.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync(ct);
        var shop = await db.Shops.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        return shop is null ? null : PublicAddressWriter.ShopStem(shop);
    }
    private Task<bool> IsVisibleAsync(PublicAddressKind kind, Guid id, CancellationToken ct) => kind == PublicAddressKind.CoffeeShop
        ? db.Shops.AsNoTracking().AnyAsync(s => s.Id == id && !s.IsHidden, ct)
        : db.Roasters.AsNoTracking().AnyAsync(s => s.Id == id, ct);
    private static PublicAddressMetadata Metadata(PublicAddressKind kind, PublicAddress row, bool alias = false) =>
        new(row.OwnerId!.Value, row.Slug, $"/{(kind == PublicAddressKind.CoffeeShop ? "coffee-shops" : "roasters")}/{row.Slug}", row.Revision, alias);
    private void RequireEnabled(PublicAddressKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new PublicAddressException("Unknown address kind.", 400);
        if (!db.PublicAddressesEnabled) throw new PublicAddressException("Public addresses are not enabled.", 503);
    }
    private static bool IsAddressConflict(DbUpdateException ex) => ex.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } constraint
    } && (constraint.StartsWith("IX_CoffeeShopPublicAddresses_", StringComparison.Ordinal)
        || constraint.StartsWith("IX_RoasterPublicAddresses_", StringComparison.Ordinal)
        || constraint == "IX_PublicAddressAudit_Kind_OwnerId_Revision");
}
