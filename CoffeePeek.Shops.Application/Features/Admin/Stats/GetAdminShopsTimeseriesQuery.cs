namespace CoffeePeek.Shops.Application.Features.Admin.Stats;

/// <param name="Days">Window length in days; default 30, clamped to 1..90.</param>
public record GetAdminShopsTimeseriesQuery(int? Days);
