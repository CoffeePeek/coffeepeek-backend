namespace CoffeePeek.Contract.Dtos.Admin;

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
