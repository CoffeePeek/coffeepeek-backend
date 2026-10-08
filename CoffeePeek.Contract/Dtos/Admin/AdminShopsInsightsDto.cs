namespace CoffeePeek.Contract.Dtos.Admin;

/// <summary>Shops-service insights for the admin dashboard.</summary>
/// <param name="Ratings">Rating distribution and averages over each author's latest eligible public check-in per shop.</param>
/// <param name="TopShopsByCheckIns30Days">Top 10 shops by check-ins created in the last 30 days (rolling).</param>
/// <param name="Downloads">App download redirect statistics.</param>
public record AdminShopsInsightsDto(
    AdminRatingStatsDto Ratings,
    IReadOnlyList<AdminTopShopDto> TopShopsByCheckIns30Days,
    AdminAppDownloadStatsDto Downloads);
