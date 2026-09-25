using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Review = CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review;

namespace CoffeePeek.Shops.Persistance.Repositories;

// All queries are sequential: the repository shares one scoped DbContext, which does not support concurrency.
public class AdminStatsQueryRepository(ShopsDbContext dbContext) : IAdminStatsQueryRepository
{
    private const int TopLimit = 10;
    private const string UnknownCountry = "unknown";

    public async Task<AdminShopsStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;

        var totalCoffeeShops = await dbContext.Shops.CountAsync(cancellationToken);
        var newCoffeeShopsToday = await dbContext.Shops.CountAsync(s => s.CreatedAtUtc >= today, cancellationToken);
        var totalReviews = await dbContext.Set<Review>().CountAsync(cancellationToken);
        var newReviewsToday = await dbContext.Set<Review>().CountAsync(r => r.CreatedAtUtc >= today, cancellationToken);

        return new AdminShopsStats(totalCoffeeShops, newCoffeeShopsToday, totalReviews, newReviewsToday);
    }

    public async Task<AdminShopsInsights> GetInsightsAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var ratings = await GetRatingsAsync(cancellationToken);
        var topByCheckIns = await GetTopShopsByCheckInsAsync(sinceUtc, cancellationToken);
        var topByReviews = await GetTopShopsByReviewsAsync(cancellationToken);
        var downloads = await GetDownloadsAsync(sinceUtc, cancellationToken);

        return new AdminShopsInsights(ratings, topByCheckIns, topByReviews, downloads);
    }

    public async Task<AdminShopsDailyCounts> GetDailyCountsAsync(
        DateTime fromUtcDate,
        CancellationToken cancellationToken = default)
    {
        var shops = await dbContext.Shops
            .AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtcDate)
            .GroupBy(s => s.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var reviews = await dbContext.Set<Review>()
            .AsNoTracking()
            .Where(r => !r.IsSoftDelete && r.CreatedAtUtc >= fromUtcDate)
            .GroupBy(r => r.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var checkIns = await dbContext.CheckIns
            .AsNoTracking()
            .Where(c => c.CreatedAtUtc >= fromUtcDate)
            .GroupBy(c => c.CreatedAtUtc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new AdminShopsDailyCounts(
            shops.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count),
            reviews.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count),
            checkIns.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count));
    }

    private async Task<AdminRatingAggregate> GetRatingsAsync(CancellationToken ct)
    {
        var visible = dbContext.Set<Review>().AsNoTracking().Where(r => !r.IsSoftDelete);

        // Group server-side on the integer sum (3..15); star bucketing happens in memory.
        var histogram = await visible
            .GroupBy(r => r.Rating.Place + r.Rating.Service + r.Rating.Coffee)
            .Select(g => new { Sum = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var averagePlace = await visible.AverageAsync(r => (decimal?)r.Rating.Place, ct) ?? 0m;
        var averageService = await visible.AverageAsync(r => (decimal?)r.Rating.Service, ct) ?? 0m;
        var averageCoffee = await visible.AverageAsync(r => (decimal?)r.Rating.Coffee, ct) ?? 0m;

        var total = histogram.Sum(x => x.Count);
        // Mean of per-review (P+S+C)/3 equals the grand sum over 3n — derived exactly from the histogram.
        var averageRating = total == 0
            ? 0m
            : histogram.Sum(x => (decimal)x.Sum * x.Count) / (3m * total);

        var starCounts = Enumerable.Range(1, 5)
            .Select(star => (star, histogram
                .Where(x => ToStars(x.Sum) == star)
                .Sum(x => x.Count)))
            .ToList();

        return new AdminRatingAggregate(total, averageRating, averagePlace, averageService, averageCoffee, starCounts);
    }

    // Average of three integers never lands on .5, so this equals rounding AverageRating.
    private static int ToStars(int ratingSum) =>
        Math.Clamp((int)Math.Round(ratingSum / 3m, MidpointRounding.AwayFromZero), 1, 5);

    private async Task<IReadOnlyList<AdminTopShop>> GetTopShopsByCheckInsAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var rows = await dbContext.CheckIns
            .AsNoTracking()
            .Where(c => c.CreatedAtUtc >= sinceUtc)
            .GroupBy(c => c.ShopId)
            .Select(g => new { ShopId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(TopLimit)
            .ToListAsync(ct);

        return await ResolveShopNamesAsync(rows.Select(r => (r.ShopId, r.Count)).ToList(), ct);
    }

    private async Task<IReadOnlyList<AdminTopShop>> GetTopShopsByReviewsAsync(CancellationToken ct)
    {
        var rows = await dbContext.Set<Review>()
            .AsNoTracking()
            .Where(r => !r.IsSoftDelete)
            .GroupBy(r => r.CoffeeShopId)
            .Select(g => new { ShopId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(TopLimit)
            .ToListAsync(ct);

        return await ResolveShopNamesAsync(rows.Select(r => (r.ShopId, r.Count)).ToList(), ct);
    }

    private async Task<IReadOnlyList<AdminTopShop>> ResolveShopNamesAsync(
        IReadOnlyList<(Guid ShopId, int Count)> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return [];

        var ids = rows.Select(r => r.ShopId).ToList();
        var names = await dbContext.Shops
            .AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return rows
            .Where(r => names.ContainsKey(r.ShopId))
            .OrderByDescending(r => r.Count)
            .Select(r => new AdminTopShop(r.ShopId, names[r.ShopId], r.Count))
            .ToList();
    }

    private async Task<AdminDownloadAggregate> GetDownloadsAsync(DateTime sinceUtc, CancellationToken ct)
    {
        var events = dbContext.AppDownloadRedirectEvents.AsNoTracking();

        var total = await events.CountAsync(ct);
        var since = await events.CountAsync(e => e.Timestamp >= sinceUtc, ct);

        var byChannel = await events
            .GroupBy(e => e.Channel)
            .Select(g => new
            {
                Channel = g.Key,
                Total = g.Count(),
                Since = g.Count(e => e.Timestamp >= sinceUtc)
            })
            .ToListAsync(ct);

        var countries = await events
            .Where(e => e.Timestamp >= sinceUtc)
            .GroupBy(e => e.Country)
            .Select(g => new { Country = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var topCountries = countries
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Country) ? UnknownCountry : c.Country!)
            .Select(g => (Country: g.Key, Count: g.Sum(x => x.Count)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Country, StringComparer.Ordinal)
            .Take(TopLimit)
            .ToList();

        return new AdminDownloadAggregate(
            total,
            since,
            byChannel
                .OrderByDescending(c => c.Total)
                .ThenBy(c => c.Channel, StringComparer.Ordinal)
                .Select(c => (c.Channel, c.Total, c.Since))
                .ToList(),
            topCountries);
    }
}
