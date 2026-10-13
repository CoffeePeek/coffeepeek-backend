using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

public partial class CheckInQueries
{
    public async Task<HomeCheckInDto?> GetHomeByIdAsync(Guid id, Guid? readerId, CancellationToken ct = default)
    {
        var eligible = CheckInEligibility.Public(_db);
        var row = await _db.CheckIns.AsNoTracking().Where(c => c.Kind == CheckInKind.Home
                && c.DeletedAtUtc == null && (c.UserId == readerId || eligible.Any(p => p.Id == c.Id)))
            .Include(c => c.ShopPhotos)
            .SingleOrDefaultAsync(c => c.Id == id, ct);
        return row is null ? null : HomeCheckInMapping.ToDto(row);
    }

    public async Task<HomeCheckInPage> GetHomeByUserAsync(Guid userId, int pageNumber, int pageSize,
        DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var query = HomeRows(userId);
        if (from.HasValue) query = query.Where(c => c.PreparedOn >= from.Value);
        if (to.HasValue) query = query.Where(c => c.PreparedOn <= to.Value);
        var total = await query.CountAsync(ct);
        var rows = await query.Include(c => c.ShopPhotos)
            .OrderByDescending(c => c.PreparedOn).ThenByDescending(c => c.VisitedAt).ThenByDescending(c => c.Id)
            .Skip((Math.Clamp(pageNumber, 1, 10000) - 1) * Math.Clamp(pageSize, 1, 100))
            .Take(Math.Clamp(pageSize, 1, 100)).ToArrayAsync(ct);
        return new HomeCheckInPage(rows.Select(HomeCheckInMapping.ToDto).ToArray(), total);
    }

    public async Task<HomeCalendarDayDto[]> GetHomeCalendarAsync(Guid userId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var rows = await HomeRows(userId).Where(c => c.PreparedOn >= from && c.PreparedOn <= to)
            .GroupBy(c => c.PreparedOn)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .OrderBy(g => g.Day)
            .ToArrayAsync(ct);
        return rows.Select(r => new HomeCalendarDayDto(r.Day!.Value, r.Count)).ToArray();
    }

    public async Task<HomeBeanStatsDto[]> GetHomeBeanStatsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await HomeRows(userId).Select(c => new
        {
            c.Id, c.VisitedAt, c.CoffeeProductId, c.BeanNameSnapshot, c.DoseGrams, c.TasteScore
        }).ToArrayAsync(ct);
        return rows.GroupBy(c => (c.CoffeeProductId,
                Name: c.CoffeeProductId.HasValue ? null : c.BeanNameSnapshot?.Trim().ToUpperInvariant()))
            .Select(g =>
            {
                var latestName = g.OrderByDescending(c => c.VisitedAt).ThenByDescending(c => c.Id)
                    .Select(c => c.BeanNameSnapshot).FirstOrDefault();
                var ratings = g.Where(c => c.TasteScore.HasValue).Select(c => c.TasteScore!.Value).ToArray();
                return new HomeBeanStatsDto(g.Key.CoffeeProductId, latestName, g.Count(),
                    g.Count(c => c.DoseGrams.HasValue),
                    g.Where(c => c.DoseGrams.HasValue).Sum(c => c.DoseGrams!.Value), ratings.Length,
                    ratings.Length == 0 ? null : Math.Round(ratings.Average(x => (decimal)x), 2));
            })
            .OrderByDescending(g => g.BrewCount).ThenBy(g => g.BeanName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<HomeMethodStatsDto[]> GetHomeMethodStatsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await HomeRows(userId).Select(c => new
        {
            c.BrewMethodCode, c.DoseGrams, c.TasteScore
        }).ToArrayAsync(ct);
        return rows.GroupBy(c => c.BrewMethodCode)
            .Select(g =>
            {
                var ratings = g.Where(c => c.TasteScore.HasValue).Select(c => c.TasteScore!.Value).ToArray();
                return new HomeMethodStatsDto(g.Key, g.Count(),
                    g.Count(c => c.DoseGrams.HasValue),
                    g.Where(c => c.DoseGrams.HasValue).Sum(c => c.DoseGrams!.Value), ratings.Length,
                    ratings.Length == 0 ? null : Math.Round(ratings.Average(x => (decimal)x), 2));
            })
            .OrderByDescending(g => g.BrewCount).ThenBy(g => g.BrewMethodCode, StringComparer.Ordinal)
            .ToArray();
    }

    private IQueryable<CheckIn> HomeRows(Guid userId) => _db.CheckIns.AsNoTracking()
        .Where(c => c.UserId == userId && c.Kind == CheckInKind.Home && c.DeletedAtUtc == null);

}
