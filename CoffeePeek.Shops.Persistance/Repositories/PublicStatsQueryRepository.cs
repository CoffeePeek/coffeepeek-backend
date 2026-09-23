using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using Review = CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.Review;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class PublicStatsQueryRepository(ShopsDbContext dbContext) : IPublicStatsQueryRepository
{
    public async Task<PublicPlatformStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var totalCoffeeShops = await dbContext.Shops
            .AsNoTracking()
            .CountAsync(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden, cancellationToken);

        var publishedReviews = dbContext.Set<Review>()
            .AsNoTracking()
            .Where(r => !r.IsSoftDelete);

        var totalReviews = await publishedReviews.CountAsync(cancellationToken);

        var averageRating = totalReviews == 0
            ? 0m
            : Math.Round(
                await publishedReviews.AverageAsync(
                    r => (r.Rating.Place + r.Rating.Coffee + r.Rating.Service) / 3m,
                    cancellationToken),
                1,
                MidpointRounding.AwayFromZero);

        var totalCheckIns = await dbContext.CheckIns
            .AsNoTracking()
            .CountAsync(cancellationToken);

        return new PublicPlatformStats(
            totalCoffeeShops,
            totalReviews,
            totalCheckIns,
            averageRating);
    }
}
