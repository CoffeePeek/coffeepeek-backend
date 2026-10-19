using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record ShopMenuItemSnapshot(
    string Slug,
    MenuItemAvailability Availability,
    decimal? Price,
    MenuItemSource Source);
