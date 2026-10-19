using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record ShopMenuItemDto(
    string Slug,
    string NameRu,
    string NameEn,
    CoffeeDrinkCategory Category,
    MenuItemAvailability Availability,
    decimal? Price,
    string Currency,
    MenuItemSource Source);
