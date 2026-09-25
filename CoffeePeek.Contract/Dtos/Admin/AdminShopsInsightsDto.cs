namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Shops-service insights for the admin dashboard.</summary>
/// <param name="Ratings">Rating distribution and averages over non-deleted reviews.</param>
/// <param name="TopShopsByCheckIns30Days">Top 10 shops by check-ins created in the last 30 days (rolling).</param>
/// <param name="TopShopsByReviews">Top 10 shops by all-time non-deleted review count.</param>
/// <param name="Downloads">App download redirect statistics.</param>
public record AdminShopsInsightsDto(
    AdminRatingStatsDto Ratings,
    IReadOnlyList<AdminTopShopDto> TopShopsByCheckIns30Days,
    IReadOnlyList<AdminTopShopDto> TopShopsByReviews,
    AdminAppDownloadStatsDto Downloads);

/// <summary>Review rating statistics (soft-deleted reviews excluded). Averages rounded to 2 decimals; 0 when no reviews.</summary>
/// <param name="Distribution">Always 5 buckets, Stars 1..5; a review's star = its average rating rounded to the nearest integer.</param>
public record AdminRatingStatsDto(
    int TotalReviews,
    decimal AverageRating,
    decimal AveragePlace,
    decimal AverageService,
    decimal AverageCoffee,
    IReadOnlyList<AdminRatingBucketDto> Distribution);

/// <summary>Number of reviews whose rounded average rating equals <see cref="Stars"/>.</summary>
public record AdminRatingBucketDto(int Stars, int Count);

/// <summary>A shop with an associated count (check-ins or reviews).</summary>
public record AdminTopShopDto(Guid ShopId, string Name, int Count);

/// <summary>App download redirect statistics.</summary>
/// <param name="Total">All-time redirect events.</param>
/// <param name="Last30Days">Redirect events in the last 30 days (rolling).</param>
/// <param name="ByChannel">Per-channel totals (e.g. android, ios).</param>
/// <param name="TopCountries30Days">Top 10 countries in the last 30 days; missing country reported as "unknown".</param>
public record AdminAppDownloadStatsDto(
    int Total,
    int Last30Days,
    IReadOnlyList<AdminDownloadChannelDto> ByChannel,
    IReadOnlyList<AdminDownloadCountryDto> TopCountries30Days);

/// <summary>Download redirects for one channel: all-time and last 30 days.</summary>
public record AdminDownloadChannelDto(string Channel, int Total, int Last30Days);

/// <summary>Download redirects for one country in the last 30 days.</summary>
public record AdminDownloadCountryDto(string Country, int Count);
