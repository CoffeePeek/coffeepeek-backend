namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed record ManualShopMenuItemUpdate(
    Guid DrinkDefinitionId,
    MenuItemAvailability Availability,
    decimal? Price);
