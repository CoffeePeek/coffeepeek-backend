namespace CoffeePeek.ShopsService.Controllers.Admin;

public record UpdateShopContactsRequest(string? PhoneNumber, string? Email, string? SiteLink, string? InstagramLink);
