using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.Menu;

public record UpdateShopMenuItemRequest(
    string Slug,
    MenuItemAvailability Availability,
    decimal? Price,
    int? VolumeMl);
