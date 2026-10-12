using System.Globalization;
using System.Text;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

public partial class CheckInQueries(ShopsDbContext dbContext) : ICheckInQueries
{
    private readonly ShopsDbContext _db = dbContext;

    public async Task<UnifiedCheckInDto?> GetUnifiedByIdAsync(Guid id, Guid? readerId, CancellationToken ct = default)
    {
        var eligible = CheckInEligibility.Public(_db);
        var query = _db.CheckIns.Where(c => c.Id == id && c.Kind == CheckInKind.Shop && c.DeletedAtUtc == null
            && (c.UserId == readerId || eligible.Any(p => p.Id == c.Id)));
        return (await ProjectAsync(query, readerId, ct)).SingleOrDefault();
    }

    public async Task<(decimal AverageRating, int Count)> GetStatsByShopAsync(Guid shopId, CancellationToken ct = default)
    {
        var average = await CheckInEligibility.LatestRatings(_db).Where(c => c.ShopId == shopId)
            .AverageAsync(c => (decimal?)c.Rating!.AverageRating, ct) ?? 0m;
        var count = await CheckInEligibility.Public(_db).CountAsync(c => c.ShopId == shopId, ct);
        return (Math.Round(average, 2), count);
    }

    public Task<UnifiedCheckInDto[]> GetByUserAndShopAsync(Guid userId, Guid shopId, int page = 1, int pageSize = 10, CancellationToken ct = default) =>
        ProjectAsync(_db.CheckIns.Where(c => c.UserId == userId && c.Kind == CheckInKind.Shop
            && c.ShopId == shopId && c.DeletedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
            .Skip((Math.Clamp(page, 1, 10000) - 1) * Math.Clamp(pageSize, 1, 100)).Take(Math.Clamp(pageSize, 1, 100)), userId, ct);

    public async Task<UnifiedCheckInDto?> GetModerationSnapshotAsync(Guid id, int revision, CancellationToken ct = default) =>
        (await ProjectAsync(_db.CheckIns.Where(c => c.Id == id && c.Kind == CheckInKind.Shop && c.ContentRevision == revision
            && c.DeletedAtUtc == null && c.Visibility == CheckInVisibility.Public), null, ct))
        .SingleOrDefault();

    public async Task<CheckInPage> GetUnifiedByUserAsync(Guid userId, int pageNumber, int pageSize,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = _db.CheckIns.Where(c => c.UserId == userId && c.Kind == CheckInKind.Shop
            && c.DeletedAtUtc == null);
        if (from.HasValue) query = query.Where(c => c.VisitedAt >= from.Value);
        if (to.HasValue) query = query.Where(c => c.VisitedAt < to.Value);
        var total = await query.CountAsync(ct);
        var items = await ProjectAsync(query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
            .Skip((Math.Clamp(pageNumber, 1, 10000) - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100)), userId, ct);
        return new CheckInPage(items, total);
    }

    public async Task<PublicCheckInPage> GetPublicAsync(Guid? readerId, Guid? cityId, Guid? shopId, Guid? authorId,
        int pageSize, string? cursor, CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 100) throw new ValidationException("PageSize must be between 1 and 100.");
        var query = CheckInEligibility.Public(_db);
        if (cityId.HasValue) query = query.Where(c => _db.Shops.Any(s => s.Id == c.ShopId && s.Location.CityId == cityId));
        if (shopId.HasValue) query = query.Where(c => c.ShopId == shopId);
        if (authorId.HasValue) query = query.Where(c => c.UserId == authorId);
        if (cursor != null)
        {
            var (created, id) = DecodeCursor(cursor);
            query = query.Where(c => c.CreatedAtUtc < created || (c.CreatedAtUtc == created && c.Id.CompareTo(id) < 0));
        }
        var items = await ProjectAsync(query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
            .Take(pageSize + 1), readerId, ct);
        var hasMore = items.Length > pageSize;
        items = items.Take(pageSize).ToArray();
        var next = hasMore ? EncodeCursor(items[^1].CreatedAtUtc, items[^1].Id) : null;
        return new PublicCheckInPage(items, next);
    }

    private async Task<UnifiedCheckInDto[]> ProjectAsync(IQueryable<CheckIn> query, Guid? readerId, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().Include(c => c.ShopPhotos).ToArrayAsync(ct);
        return await CheckInProjection.FromRowsAsync(_db, rows, readerId, ct);
    }

    public static string EncodeCursor(DateTime createdAt, Guid id) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{createdAt.Ticks}:{id:D}"));

    public static (DateTime CreatedAt, Guid Id) DecodeCursor(string cursor)
    {
        try
        {
            if (cursor.Length > 128) throw new FormatException();
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(parts[1], "D", out var id) || id == Guid.Empty) throw new FormatException();
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new ValidationException("Invalid check-in cursor."); }
    }
}
