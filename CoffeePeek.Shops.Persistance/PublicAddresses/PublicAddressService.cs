using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Persistance.PublicAddresses;

// Every kind dispatch is an explicit switch that rejects unknown kinds; nothing defaults to another registry.
public sealed class PublicAddressService(ShopsDbContext db) : IPublicAddressService
{
    public Task<PublicAddressMetadata?> ResolveAsync(PublicAddressKind kind, string slug, CancellationToken ct)
    {
        RequireEnabled(kind);
        var normalized = slug.ToLowerInvariant();
        if (!PublicSlug.IsValid(normalized)) throw new PublicAddressException("Invalid public address.", 400);
        return kind switch
        {
            PublicAddressKind.CoffeeShop => ResolveAsync<CoffeeShopPublicAddress>(kind, normalized, ct),
            PublicAddressKind.Roaster => ResolveAsync<RoasterPublicAddress>(kind, normalized, ct),
            PublicAddressKind.City => ResolveAsync<CityPublicAddress>(kind, normalized, ct),
            PublicAddressKind.CoffeeZone => ResolveAsync<CoffeeZonePublicAddress>(kind, normalized, ct),
            _ => throw UnknownKind()
        };
    }

    private async Task<PublicAddressMetadata?> ResolveAsync<T>(PublicAddressKind kind, string normalized, CancellationToken ct) where T : PublicAddress
    {
        var address = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(a => a.Slug == normalized, ct);
        if (address?.OwnerId is not { } ownerId) return null;
        // Never trust a cached detail payload to determine current public visibility.
        if (!await IsVisibleAsync(kind, ownerId, ct)) return null;
        var current = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(a => a.OwnerId == ownerId && a.State == PublicAddressState.Canonical, ct);
        return current is null ? null : Metadata(kind, current, normalized != current.Slug);
    }

    public Task<PublicAddressMetadata[]> GetByIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (ids.Length is < 1 or > 100 || ids.Contains(Guid.Empty)) throw new PublicAddressException("Supply 1–100 nonempty entity IDs.", 400);
        var distinct = ids.Distinct().ToArray();
        return kind switch
        {
            PublicAddressKind.CoffeeShop => GetByIdsAsync<CoffeeShopPublicAddress>(kind, distinct, ct),
            PublicAddressKind.Roaster => GetByIdsAsync<RoasterPublicAddress>(kind, distinct, ct),
            PublicAddressKind.City => GetByIdsAsync<CityPublicAddress>(kind, distinct, ct),
            PublicAddressKind.CoffeeZone => GetByIdsAsync<CoffeeZonePublicAddress>(kind, distinct, ct),
            _ => throw UnknownKind()
        };
    }

    private async Task<PublicAddressMetadata[]> GetByIdsAsync<T>(PublicAddressKind kind, Guid[] ids, CancellationToken ct) where T : PublicAddress
    {
        var visible = await VisibleIdsAsync(kind, ids, ct);
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
            return await PublicAddressTransaction.RunAsync(db, () => kind switch
            {
                PublicAddressKind.CoffeeShop => ChangeAsync<CoffeeShopPublicAddress>(kind, id, request, actorId, ct),
                PublicAddressKind.Roaster => ChangeAsync<RoasterPublicAddress>(kind, id, request, actorId, ct),
                PublicAddressKind.City => ChangeAsync<CityPublicAddress>(kind, id, request, actorId, ct),
                PublicAddressKind.CoffeeZone => ChangeAsync<CoffeeZonePublicAddress>(kind, id, request, actorId, ct),
                _ => throw UnknownKind()
            }, ct);
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
        if (await GetStemAsync(kind, id, ct) is null) throw new PublicAddressException("Entity not found.", 404);
        return await db.PublicAddressAudit.AsNoTracking().Where(a => a.Kind == kind && a.OwnerId == id && a.Revision > afterRevision)
            .OrderBy(a => a.Revision).Take(limit)
            .Select(a => new PublicAddressHistoryItem(a.Revision, a.PreviousSlug, a.Slug, a.Action, a.ActorId, a.Reason, a.AtUtc)).ToArrayAsync(ct);
    }

    public Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (limit is < 1 or > 100 || actorId == Guid.Empty) throw new PublicAddressException("Limit must be 1–100 and actor required.", 400);
        Task<PublicAddressBackfillResult> Execute() => kind switch
        {
            PublicAddressKind.CoffeeShop => BackfillAsync<CoffeeShopPublicAddress>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.Roaster => BackfillAsync<RoasterPublicAddress>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.City => BackfillAsync<CityPublicAddress>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.CoffeeZone => BackfillAsync<CoffeeZonePublicAddress>(kind, afterId, limit, dryRun, actorId, ct),
            _ => throw UnknownKind()
        };
        return dryRun ? Execute() : PublicAddressTransaction.RunAsync(db, Execute, ct);
    }

    private async Task<PublicAddressBackfillResult> BackfillAsync<T>(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct) where T : PublicAddress
    {
        var owners = await ListOwnersAsync(kind, afterId, limit, ct);
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

    // Always projected: a CoffeeZone entity is never materialized here (its Polygon is a JSON complex collection).
    private Task<OwnerStem[]> ListOwnersAsync(PublicAddressKind kind, Guid? afterId, int limit, CancellationToken ct) => kind switch
    {
        PublicAddressKind.CoffeeShop => db.Shops.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
            .Select(s => new OwnerStem(s.Id, s.Name, s.Location == null ? null : s.Location.Address)).ToArrayAsync(ct),
        PublicAddressKind.Roaster => db.Roasters.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
            .Select(s => new OwnerStem(s.Id, s.Name, null)).ToArrayAsync(ct),
        // D-02: every city is public, so every city is backfilled.
        PublicAddressKind.City => db.Cities.AsNoTracking().Where(c => !afterId.HasValue || c.Id.CompareTo(afterId.Value) > 0).OrderBy(c => c.Id).Take(limit + 1)
            .Select(c => new OwnerStem(c.Id, c.Name, null)).ToArrayAsync(ct),
        // D-02: drafts and archived zones reserve no address; they are allocated on first publication.
        PublicAddressKind.CoffeeZone => db.CoffeeZones.AsNoTracking()
            .Where(z => z.Status == CoffeeZoneStatus.Published && (!afterId.HasValue || z.Id.CompareTo(afterId.Value) > 0)).OrderBy(z => z.Id).Take(limit + 1)
            .Select(z => new OwnerStem(z.Id, z.Name, null)).ToArrayAsync(ct),
        _ => throw UnknownKind()
    };

    // Owner existence for admin operations. No status filter: admins may initialize or inspect a draft zone;
    // public visibility is enforced only by IsVisibleAsync / VisibleIdsAsync.
    private async Task<string?> GetStemAsync(PublicAddressKind kind, Guid id, CancellationToken ct) => kind switch
    {
        PublicAddressKind.CoffeeShop => await db.Shops.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct) is { } shop
            ? PublicAddressWriter.ShopStem(shop) : null,
        PublicAddressKind.Roaster => await db.Roasters.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync(ct),
        PublicAddressKind.City => await db.Cities.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name).SingleOrDefaultAsync(ct),
        PublicAddressKind.CoffeeZone => await db.CoffeeZones.AsNoTracking().Where(z => z.Id == id).Select(z => z.Name).SingleOrDefaultAsync(ct),
        _ => throw UnknownKind()
    };

    private Task<bool> IsVisibleAsync(PublicAddressKind kind, Guid id, CancellationToken ct) => kind switch
    {
        PublicAddressKind.CoffeeShop => db.Shops.AsNoTracking().AnyAsync(s => s.Id == id && !s.IsHidden, ct),
        PublicAddressKind.Roaster => db.Roasters.AsNoTracking().AnyAsync(s => s.Id == id, ct),
        // D-02: matches the unfiltered GET api/Catalogs/cities.
        PublicAddressKind.City => db.Cities.AsNoTracking().AnyAsync(c => c.Id == id, ct),
        // D-02: matches GET api/Map; Draft/Archived resolve exactly like unknown slugs.
        PublicAddressKind.CoffeeZone => db.CoffeeZones.AsNoTracking().AnyAsync(z => z.Id == id && z.Status == CoffeeZoneStatus.Published, ct),
        _ => throw UnknownKind()
    };

    private Task<Guid[]> VisibleIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct) => kind switch
    {
        PublicAddressKind.CoffeeShop => db.Shops.AsNoTracking().Where(s => ids.Contains(s.Id) && !s.IsHidden).Select(s => s.Id).ToArrayAsync(ct),
        PublicAddressKind.Roaster => db.Roasters.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArrayAsync(ct),
        PublicAddressKind.City => db.Cities.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToArrayAsync(ct),
        PublicAddressKind.CoffeeZone => db.CoffeeZones.AsNoTracking()
            .Where(z => ids.Contains(z.Id) && z.Status == CoffeeZoneStatus.Published).Select(z => z.Id).ToArrayAsync(ct),
        _ => throw UnknownKind()
    };

    // Canonical paths are built only from these fixed literals plus the stored, validated slug (never request input).
    private static PublicAddressMetadata Metadata(PublicAddressKind kind, PublicAddress row, bool alias = false) =>
        new(row.OwnerId!.Value, row.Slug, $"/{PathSegment(kind)}/{row.Slug}", row.Revision, alias);

    private static string PathSegment(PublicAddressKind kind) => kind switch
    {
        PublicAddressKind.CoffeeShop => "coffee-shops",
        PublicAddressKind.Roaster => "roasters",
        PublicAddressKind.City => "cities",
        PublicAddressKind.CoffeeZone => "coffee-zones",
        _ => throw UnknownKind()
    };

    private static PublicAddressException UnknownKind() => new("Unknown address kind.", 400);

    private void RequireEnabled(PublicAddressKind kind)
    {
        if (!Enum.IsDefined(kind)) throw UnknownKind();
        if (!db.PublicAddressesEnabled) throw new PublicAddressException("Public addresses are not enabled.", 503);
    }

    private static bool IsAddressConflict(DbUpdateException ex) => ex.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } constraint
    } && (constraint.StartsWith("IX_CoffeeShopPublicAddresses_", StringComparison.Ordinal)
        || constraint.StartsWith("IX_RoasterPublicAddresses_", StringComparison.Ordinal)
        || constraint.StartsWith("IX_CityPublicAddresses_", StringComparison.Ordinal)
        || constraint.StartsWith("IX_CoffeeZonePublicAddresses_", StringComparison.Ordinal)
        || constraint == "IX_PublicAddressAudit_Kind_OwnerId_Revision");
}
