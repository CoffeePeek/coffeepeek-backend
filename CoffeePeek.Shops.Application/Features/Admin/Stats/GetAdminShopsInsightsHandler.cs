using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.Admin.Stats;

public static class GetAdminShopsInsightsHandler
{
    private const int RecentWindowDays = 30;

    public static async Task<Response<AdminShopsInsightsDto>> Handle(
        GetAdminShopsInsightsQuery _,
        IAdminStatsQueryRepository repository,
        CancellationToken ct)
    {
        var insights = await repository.GetInsightsAsync(DateTime.UtcNow.AddDays(-RecentWindowDays), ct);
        var ratings = insights.Ratings;
        var downloads = insights.Downloads;

        return Response<AdminShopsInsightsDto>.Success(new AdminShopsInsightsDto(
            Ratings: new AdminRatingStatsDto(
                TotalReviews: ratings.TotalReviews,
                AverageRating: Round2(ratings.AverageRating),
                AveragePlace: Round2(ratings.AveragePlace),
                AverageService: Round2(ratings.AverageService),
                AverageCoffee: Round2(ratings.AverageCoffee),
                Distribution: ratings.StarCounts.Select(x => new AdminRatingBucketDto(x.Stars, x.Count)).ToList()),
            TopShopsByCheckIns30Days: insights.TopShopsByCheckIns.Select(ToDto).ToList(),
            TopShopsByReviews: insights.TopShopsByReviews.Select(ToDto).ToList(),
            Downloads: new AdminAppDownloadStatsDto(
                Total: downloads.Total,
                Last30Days: downloads.SinceCount,
                ByChannel: downloads.ByChannel
                    .Select(c => new AdminDownloadChannelDto(c.Channel, c.Total, c.SinceCount))
                    .ToList(),
                TopCountries30Days: downloads.TopCountriesSince
                    .Select(c => new AdminDownloadCountryDto(c.Country, c.Count))
                    .ToList())));
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static AdminTopShopDto ToDto(AdminTopShop shop) => new(shop.ShopId, shop.Name, shop.Count);
}
