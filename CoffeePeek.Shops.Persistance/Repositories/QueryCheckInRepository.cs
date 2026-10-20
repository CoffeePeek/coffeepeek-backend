using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryCheckInRepository(ShopsDbContext dbContext) : IQueryCheckInRepository
{
    private readonly DbSet<CheckIn> _repository = dbContext.CheckIns;

    public Task<CheckIn?> GetTrackedByIdAsync(Guid id, CancellationToken ct = default) =>
        _repository.Include(x => x.ShopPhotos).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<(bool IsHelpful, int HelpfulCount, bool Changed)> SetHelpfulAsync(Guid id, Guid userId, bool helpful, CancellationToken ct = default)
    {
        // Serialize eligibility and votes with edit/delete/moderation on the SAME canonical row.
        // The unique vote key still makes duplicate requests idempotent.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"CanonicalCheckIns\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        var eligible = await Queries.CheckInEligibility.Public(dbContext).AnyAsync(c => c.Id == id, ct);
        if (!eligible) throw new CoffeePeek.Shared.Kernel.Exceptions.NotFoundException("Check-in not found.");
        var changed = helpful
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                WITH inserted AS (
                    INSERT INTO "CheckInHelpfulVotes" ("CheckInId", "UserId", "CreatedAtUtc") VALUES ({id}, {userId}, {DateTime.UtcNow})
                    ON CONFLICT ("CheckInId", "UserId") DO NOTHING RETURNING "CheckInId"
                ) UPDATE "CanonicalCheckIns" SET "HelpfulCount" = "HelpfulCount" + 1, "RowVersion" = {Guid.NewGuid()}
                  WHERE "Id" = {id} AND EXISTS (SELECT 1 FROM inserted)
                """, ct)
            : await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                WITH deleted AS (
                    DELETE FROM "CheckInHelpfulVotes" WHERE "CheckInId" = {id} AND "UserId" = {userId} RETURNING "CheckInId"
                ) UPDATE "CanonicalCheckIns" SET "HelpfulCount" = GREATEST("HelpfulCount" - 1, 0), "RowVersion" = {Guid.NewGuid()}
                  WHERE "Id" = {id} AND EXISTS (SELECT 1 FROM deleted)
                """, ct);
        var count = await _repository.AsNoTracking().Where(c => c.Id == id).Select(c => c.HelpfulCount).SingleAsync(ct);
        var isHelpful = await dbContext.CheckInHelpfulVotes.AnyAsync(v => v.CheckInId == id && v.UserId == userId, ct);
        return (isHelpful, count, changed == 1);
    }

    public async Task<bool> ArePhotoKeysUsedAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) =>
        await dbContext.CheckInPhotos.AnyAsync(p => keys.Contains(p.StorageKey), ct)
        || await dbContext.ShopPhotos.AnyAsync(p => keys.Contains(p.StorageKey), ct)
        || await dbContext.RoasterPhotos.AnyAsync(p => keys.Contains(p.StorageKey), ct)
        || await dbContext.Set<CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.ShopMenuPhoto>()
            .AnyAsync(p => keys.Contains(p.StorageKey), ct);

    public Task<bool> Exists(Guid userId, Guid coffeeShopId, CancellationToken ct = default)
    {
        return _repository.AnyAsync(x => x.UserId == userId && x.Kind == CheckInKind.Shop
            && x.ShopId == coffeeShopId && x.DeletedAtUtc == null, ct);
    }

    public Task LockUserForCheckInAsync(Guid userId, CancellationToken ct = default) =>
        // Transaction-scoped: released on commit/rollback of the Wolverine handler transaction.
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"checkin:" + userId}, 0))", ct);

    public Task<bool> ExistsSinceAsync(Guid userId, Guid shopId, DateTime sinceUtc, CancellationToken ct = default)
    {
        return _repository.AnyAsync(x => x.UserId == userId && x.Kind == CheckInKind.Shop
            && x.ShopId == shopId && x.CreatedAtUtc >= sinceUtc, ct);
    }

    public Task<int> CountSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        return _repository.CountAsync(x => x.UserId == userId && x.Kind == CheckInKind.Shop
            && x.CreatedAtUtc >= sinceUtc, ct);
    }

    public async Task<IReadOnlyList<CheckIn>> GetByUserAndShopAsync(
        Guid userId,
        Guid shopId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        return await _repository
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Kind == CheckInKind.Shop
                && x.ShopId == shopId && x.DeletedAtUtc == null)
            .Include(x => x.ShopPhotos)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public void Add(CheckIn checkIn)
    {
        _repository.Add(checkIn);
    }

    public Task<List<Guid>> GetVisitedShopIdsAsync(Guid userId, CancellationToken ct = default)
    {
        return _repository
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Kind == CheckInKind.Shop && x.ShopId != null && x.DeletedAtUtc == null)
            .Select(x => x.ShopId!.Value)
            .ToListAsync(ct);
    }

    public Task<int> GetCheckInCountByCoffeeShopIdAsync(Guid coffeeShopId, CancellationToken ct = default)
    {
        return Queries.CheckInEligibility.Public(dbContext)
            .AsNoTracking()
            .CountAsync(x => x.ShopId == coffeeShopId, ct);
    }

    public async Task<Dictionary<Guid, int>> GetCheckInCountsByShopIdsAsync(IEnumerable<Guid> shopIds, CancellationToken ct = default)
    {
        var shopIdList = shopIds.ToList();

        var counts = await Queries.CheckInEligibility.Public(dbContext)
            .AsNoTracking()
            .Where(x => x.Kind == CheckInKind.Shop && x.ShopId != null && shopIdList.Contains(x.ShopId.Value))
            .GroupBy(x => x.ShopId!.Value)
            .Select(g => new { ShopId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(c => c.ShopId, c => c.Count);
    }

    public Task<bool> ExistsByIdAsync(Guid checkInId, CancellationToken ct = default) =>
        _repository.AsNoTracking().AnyAsync(x => x.Id == checkInId, ct);

    public Task<Guid?> GetUserIdByIdAsync(Guid checkInId, CancellationToken ct = default) =>
        _repository
            .AsNoTracking()
            .Where(x => x.Id == checkInId)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(ct);

    public async Task<(Guid Id, string Name)?> GetPublishedCoffeeProductBySlugAsync(string slug,
        CancellationToken ct = default)
    {
        var product = await dbContext.CoffeeProducts.AsNoTracking()
            .Where(p => p.PublicSlug == slug && p.Status == CoffeePublicationStatus.Published)
            .Select(p => new { p.Id, p.Name })
            .SingleOrDefaultAsync(ct);
        return product is null ? null : (product.Id, product.Name);
    }
}
