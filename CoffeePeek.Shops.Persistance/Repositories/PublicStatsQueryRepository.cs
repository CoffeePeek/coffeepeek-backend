using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class PublicStatsQueryRepository(ShopsDbContext dbContext) : IPublicStatsQueryRepository
{
    public async Task<PublicPlatformStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var totalCoffeeShops = await dbContext.Shops
            .AsNoTracking()
            .CountAsync(s => s.Status == CoffeeShopStatus.Active && !s.IsHidden, cancellationToken);

        var publishedReviews = CheckInEligibility.Public(dbContext).AsNoTracking();
        var selectedRatings = CheckInEligibility.LatestRatings(dbContext);

        var totalReviews = await publishedReviews.CountAsync(cancellationToken);

        var averageRating = !await selectedRatings.AnyAsync(cancellationToken)
            ? 0m
            : Math.Round(
                await selectedRatings.AverageAsync(
                    r => (r.Rating!.Place + r.Rating.Coffee + r.Rating.Service) / 3m,
                    cancellationToken),
                1,
                MidpointRounding.AwayFromZero);

        var totalCheckIns = totalReviews;

        return new PublicPlatformStats(
            totalCoffeeShops,
            totalCheckIns,
            averageRating);
    }
}
