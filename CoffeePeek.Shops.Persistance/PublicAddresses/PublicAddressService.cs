using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
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
            PublicAddressKind.CoffeeShop => ResolveAsync<CoffeeShop>(kind, normalized, ct),
            PublicAddressKind.Roaster => ResolveAsync<Roaster>(kind, normalized, ct),
            PublicAddressKind.City => ResolveAsync<City>(kind, normalized, ct),
            PublicAddressKind.CoffeeZone => ResolveAsync<CoffeeZone>(kind, normalized, ct),
            PublicAddressKind.CoffeeBean => ResolveAsync<CoffeeBean>(kind, normalized, ct),
            PublicAddressKind.Equipment => ResolveAsync<Equipment>(kind, normalized, ct),
            PublicAddressKind.BrewMethod => ResolveAsync<BrewMethod>(kind, normalized, ct),
            PublicAddressKind.ShopTag => ResolveAsync<ShopTag>(kind, normalized, ct),
            _ => throw UnknownKind()
        };
    }

    private async Task<PublicAddressMetadata?> ResolveAsync<T>(PublicAddressKind kind, string normalized, CancellationToken ct) where T : class, IPublicSlugOwner
    {
        var owner = await db.Set<T>().AsNoTracking().Where(a => a.PublicSlug == normalized)
            .Select(a => new SlugData(a.Id, a.PublicSlug!, a.SlugRevision)).SingleOrDefaultAsync(ct);
        if (owner is null || !await IsVisibleAsync(kind, owner.Id, ct)) return null;
        return Metadata(kind, owner);
    }

    public Task<Guid[]> ResolveIdsAsync(PublicAddressKind kind, string[] slugs, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (slugs.Length > 100 || slugs.Any(s => !PublicSlug.IsValid(s?.ToLowerInvariant())))
            throw new PublicAddressException("Supply at most 100 valid public slugs.", 400);
        var normalized = slugs.Select(s => s.ToLowerInvariant()).Distinct().ToArray();
        return kind switch
        {
            PublicAddressKind.CoffeeShop => ResolveIdsAsync<CoffeeShop>(kind, normalized, ct),
            PublicAddressKind.Roaster => ResolveIdsAsync<Roaster>(kind, normalized, ct),
            PublicAddressKind.City => ResolveIdsAsync<City>(kind, normalized, ct),
            PublicAddressKind.CoffeeZone => ResolveIdsAsync<CoffeeZone>(kind, normalized, ct),
            PublicAddressKind.CoffeeBean => ResolveIdsAsync<CoffeeBean>(kind, normalized, ct),
            PublicAddressKind.Equipment => ResolveIdsAsync<Equipment>(kind, normalized, ct),
            PublicAddressKind.BrewMethod => ResolveIdsAsync<BrewMethod>(kind, normalized, ct),
            PublicAddressKind.ShopTag => ResolveIdsAsync<ShopTag>(kind, normalized, ct),
            _ => throw UnknownKind()
        };
    }

    private async Task<Guid[]> ResolveIdsAsync<T>(PublicAddressKind kind, string[] slugs, CancellationToken ct) where T : class, IPublicSlugOwner
    {
        if (slugs.Length == 0) return [];
        var rows = await db.Set<T>().AsNoTracking().Where(a => slugs.Contains(a.PublicSlug!))
            .Select(a => new { Slug = a.PublicSlug!, a.Id }).ToArrayAsync(ct);
        var visible = await GetForReadAsync(kind, rows.Select(x => x.Id).Distinct().ToArray(), ct);
        var ids = visible.Select(x => x.EntityId).ToHashSet();
        if (rows.Length != slugs.Length || rows.Any(x => !ids.Contains(x.Id)))
            throw new PublicAddressException("Entity not found.", 404);
        return rows.Select(x => x.Id).Distinct().ToArray();
    }

    public Task<PublicAddressMetadata[]> GetByIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (ids.Length is < 1 or > 100 || ids.Contains(Guid.Empty)) throw new PublicAddressException("Supply 1–100 nonempty entity IDs.", 400);
        return GetForReadAsync(kind, ids, ct);
    }

    // Read-model composition is a single batch per kind, including large map responses.
    // The internal HTTP endpoint keeps its independent 100-ID input limit.
    public Task<PublicAddressMetadata[]> GetForReadAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (ids.Length > 20_000 || ids.Contains(Guid.Empty)) throw new PublicAddressException("Invalid address read batch.", 400);
        if (ids.Length == 0) return Task.FromResult(Array.Empty<PublicAddressMetadata>());
        var distinct = ids.Distinct().ToArray();
        return kind switch
        {
            PublicAddressKind.CoffeeShop => GetByIdsAsync<CoffeeShop>(kind, distinct, ct),
            PublicAddressKind.Roaster => GetByIdsAsync<Roaster>(kind, distinct, ct),
            PublicAddressKind.City => GetByIdsAsync<City>(kind, distinct, ct),
            PublicAddressKind.CoffeeZone => GetByIdsAsync<CoffeeZone>(kind, distinct, ct),
            PublicAddressKind.CoffeeBean => GetByIdsAsync<CoffeeBean>(kind, distinct, ct),
            PublicAddressKind.Equipment => GetByIdsAsync<Equipment>(kind, distinct, ct),
            PublicAddressKind.BrewMethod => GetByIdsAsync<BrewMethod>(kind, distinct, ct),
            PublicAddressKind.ShopTag => GetByIdsAsync<ShopTag>(kind, distinct, ct),
            _ => throw UnknownKind()
        };
    }

    private async Task<PublicAddressMetadata[]> GetByIdsAsync<T>(PublicAddressKind kind, Guid[] ids, CancellationToken ct) where T : class, IPublicSlugOwner
    {
        var visible = await VisibleIdsAsync(kind, ids, ct);
        var rows = await db.Set<T>().AsNoTracking().Where(a => visible.Contains(a.Id) && a.PublicSlug != null)
            .Select(a => new SlugData(a.Id, a.PublicSlug!, a.SlugRevision)).ToArrayAsync(ct);
        var byOwner = rows.ToDictionary(a => a.Id);
        return ids.Where(byOwner.ContainsKey).Select(id => Metadata(kind, byOwner[id])).ToArray();
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
                PublicAddressKind.CoffeeShop => ChangeAsync<CoffeeShop>(kind, id, request, actorId, ct),
                PublicAddressKind.Roaster => ChangeAsync<Roaster>(kind, id, request, actorId, ct),
                PublicAddressKind.City => ChangeAsync<City>(kind, id, request, actorId, ct),
                PublicAddressKind.CoffeeZone => ChangeAsync<CoffeeZone>(kind, id, request, actorId, ct),
                PublicAddressKind.CoffeeBean => ChangeAsync<CoffeeBean>(kind, id, request, actorId, ct),
                PublicAddressKind.Equipment => ChangeAsync<Equipment>(kind, id, request, actorId, ct),
                PublicAddressKind.BrewMethod => ChangeAsync<BrewMethod>(kind, id, request, actorId, ct),
                PublicAddressKind.ShopTag => ChangeAsync<ShopTag>(kind, id, request, actorId, ct),
                _ => throw UnknownKind()
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { throw new PublicAddressException("Address changed; reload and retry.", 409); }
        catch (DbUpdateException ex) when (IsAddressConflict(ex)) { throw new PublicAddressException("Address is reserved or changed concurrently.", 409); }
    }

    private async Task<PublicAddressMetadata> ChangeAsync<T>(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request, Guid actorId, CancellationToken ct) where T : class, IPublicSlugOwner
    {
        var stem = await GetStemAsync(kind, id, ct) ?? throw new PublicAddressException("Entity not found.", 404);
        var owner = await db.Set<T>().SingleAsync(a => a.Id == id, ct);
        if (owner.PublicSlug is not null && request.ExpectedRevision == 0 &&
            (request.Slug is null || request.Slug == owner.PublicSlug)) return Metadata(kind, owner);
        if (owner.SlugRevision != request.ExpectedRevision) throw new PublicAddressException("Expected revision does not match the current address.", 409);
        if (owner.PublicSlug is not null && request.Slug is null) throw new PublicAddressException("A slug is required for renaming.", 400);
        var slug = request.Slug ?? await PublicAddressWriter.FindCandidateAsync<T>(db, stem, id, null, ct);
        if (await db.Set<T>().AnyAsync(a => a.Id != id && a.PublicSlug == slug, ct))
            throw new PublicAddressException("This slug is already in use.", 409);
        owner.SetPublicSlug(slug);
        await db.SaveChangesAsync(ct);
        return Metadata(kind, owner);
    }



    public async Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct)
    {
        RequireEnabled(kind);
        if (limit is < 1 or > 100 || actorId == Guid.Empty) throw new PublicAddressException("Limit must be 1–100 and actor required.", 400);
        Task<PublicAddressBackfillResult> Execute() => kind switch
        {
            PublicAddressKind.CoffeeShop => BackfillAsync<CoffeeShop>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.Roaster => BackfillAsync<Roaster>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.City => BackfillAsync<City>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.CoffeeZone => BackfillAsync<CoffeeZone>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.CoffeeBean => BackfillAsync<CoffeeBean>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.Equipment => BackfillAsync<Equipment>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.BrewMethod => BackfillAsync<BrewMethod>(kind, afterId, limit, dryRun, actorId, ct),
            PublicAddressKind.ShopTag => BackfillAsync<ShopTag>(kind, afterId, limit, dryRun, actorId, ct),
            _ => throw UnknownKind()
        };
        try
        {
            return dryRun ? await Execute() : await PublicAddressTransaction.RunAsync(db, Execute, ct);
        }
        // A concurrent out-of-band writer took the slug: 409, not 500 (live gate G-S10b); the owned transaction already rolled back the page.
        catch (DbUpdateException ex) when (IsAddressConflict(ex)) { throw new PublicAddressException("Address is reserved or changed concurrently.", 409); }
    }

    private async Task<PublicAddressBackfillResult> BackfillAsync<T>(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct) where T : class, IPublicSlugOwner
    {
        var owners = await ListOwnersAsync(kind, afterId, limit, ct);
        var proposals = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<PublicAddressBackfillItem>();
        foreach (var owner in owners.Take(limit))
        {
            var current = await db.Set<T>().SingleAsync(a => a.Id == owner.Id, ct);
            var initialized = current.PublicSlug is not null;
            var stem = string.IsNullOrWhiteSpace(owner.Address) ? owner.Name : $"{owner.Name} {owner.Address}";
            var slug = current.PublicSlug ?? await PublicAddressWriter.FindCandidateAsync<T>(db, stem, owner.Id, proposals, ct);
            proposals.Add(slug);
            if (!dryRun && !initialized) current.SetPublicSlug(slug);
            items.Add(new PublicAddressBackfillItem(owner.Id, slug, slug != PublicSlug.Generate(stem), initialized));
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
        PublicAddressKind.CoffeeBean => db.CoffeeBeans.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
            .Select(s => new OwnerStem(s.Id, s.Name, null)).ToArrayAsync(ct),
        PublicAddressKind.Equipment => db.Equipments.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
            .Select(s => new OwnerStem(s.Id, s.Name, null)).ToArrayAsync(ct),
        PublicAddressKind.BrewMethod => db.BrewMethods.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
            .Select(s => new OwnerStem(s.Id, s.Name, null)).ToArrayAsync(ct),
        PublicAddressKind.ShopTag => db.ShopTags.AsNoTracking().Where(s => !afterId.HasValue || s.Id.CompareTo(afterId.Value) > 0).OrderBy(s => s.Id).Take(limit + 1)
            .Select(s => new OwnerStem(s.Id, s.Slug, null)).ToArrayAsync(ct),
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
        PublicAddressKind.CoffeeBean => await db.CoffeeBeans.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync(ct),
        PublicAddressKind.Equipment => await db.Equipments.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync(ct),
        PublicAddressKind.BrewMethod => await db.BrewMethods.AsNoTracking().Where(s => s.Id == id).Select(s => s.Name).SingleOrDefaultAsync(ct),
        PublicAddressKind.ShopTag => await db.ShopTags.AsNoTracking().Where(s => s.Id == id).Select(s => s.Slug).SingleOrDefaultAsync(ct),
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
        PublicAddressKind.CoffeeBean => db.CoffeeBeans.AsNoTracking().AnyAsync(s => s.Id == id, ct),
        PublicAddressKind.Equipment => db.Equipments.AsNoTracking().AnyAsync(s => s.Id == id, ct),
        PublicAddressKind.BrewMethod => db.BrewMethods.AsNoTracking().AnyAsync(s => s.Id == id, ct),
        PublicAddressKind.ShopTag => db.ShopTags.AsNoTracking().AnyAsync(s => s.Id == id && s.IsActive, ct),
        _ => throw UnknownKind()
    };

    private Task<Guid[]> VisibleIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct) => kind switch
    {
        PublicAddressKind.CoffeeShop => db.Shops.AsNoTracking().Where(s => ids.Contains(s.Id) && !s.IsHidden).Select(s => s.Id).ToArrayAsync(ct),
        PublicAddressKind.Roaster => db.Roasters.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArrayAsync(ct),
        PublicAddressKind.City => db.Cities.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToArrayAsync(ct),
        PublicAddressKind.CoffeeZone => db.CoffeeZones.AsNoTracking()
            .Where(z => ids.Contains(z.Id) && z.Status == CoffeeZoneStatus.Published).Select(z => z.Id).ToArrayAsync(ct),
        PublicAddressKind.CoffeeBean => db.CoffeeBeans.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArrayAsync(ct),
        PublicAddressKind.Equipment => db.Equipments.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArrayAsync(ct),
        PublicAddressKind.BrewMethod => db.BrewMethods.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArrayAsync(ct),
        PublicAddressKind.ShopTag => db.ShopTags.AsNoTracking().Where(s => ids.Contains(s.Id) && s.IsActive).Select(s => s.Id).ToArrayAsync(ct),
        _ => throw UnknownKind()
    };

    // Canonical paths are built only from these fixed literals plus the stored, validated slug (never request input).
    private static PublicAddressMetadata Metadata(PublicAddressKind kind, IPublicSlugOwner row) =>
        new(row.Id, row.PublicSlug!, $"/{PathSegment(kind)}/{row.PublicSlug}", row.SlugRevision);

    private sealed record SlugData(Guid Id, string Slug, long Revision);

    private static PublicAddressMetadata Metadata(PublicAddressKind kind, SlugData row) =>
        new(row.Id, row.Slug, $"/{PathSegment(kind)}/{row.Slug}", row.Revision);

    private static string PathSegment(PublicAddressKind kind) => kind switch
    {
        PublicAddressKind.CoffeeShop => "coffee-shops",
        PublicAddressKind.Roaster => "roasters",
        PublicAddressKind.City => "cities",
        PublicAddressKind.CoffeeZone => "coffee-zones",
        PublicAddressKind.CoffeeBean => "catalogs/beans",
        PublicAddressKind.Equipment => "catalogs/equipments",
        PublicAddressKind.BrewMethod => "catalogs/brew-methods",
        PublicAddressKind.ShopTag => "catalogs/shop-tags",
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
    } && (constraint.EndsWith("_Slug", StringComparison.Ordinal) || constraint == "IX_ShopTags_PublicSlug");
}
