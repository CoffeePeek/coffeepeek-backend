namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

/// <summary>Public contact details of a published shop, as returned to admin and owner APIs.</summary>
public record AdminShopContactsDto(
    string? PhoneNumber,
    string? Email,
    string? SiteLink,
    string? InstagramLink);
