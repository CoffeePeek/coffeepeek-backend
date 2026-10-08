namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopChangeContactsDto(
    string? PhoneNumber,
    string? Email,
    string? SiteLink,
    string? InstagramLink);
