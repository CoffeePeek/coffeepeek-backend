namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

/// <summary>Address and coordinates of a published shop, as returned to admin and owner APIs.</summary>
public record AdminShopLocationDto(
    Guid CityId,
    string Address,
    decimal? Latitude,
    decimal? Longitude);
