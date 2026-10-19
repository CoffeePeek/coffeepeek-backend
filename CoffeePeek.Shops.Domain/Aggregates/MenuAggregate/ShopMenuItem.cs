using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed class ShopMenuItem : Entity<Guid>
{
    public Guid ShopMenuId { get; private set; }
    public Guid DrinkDefinitionId { get; private set; }
    public MenuItemAvailability Availability { get; private set; }
    public decimal? Price { get; private set; }

    public MenuItemSource Source { get; private set; }
    public CoffeeDrinkKind Kind { get; private set; }
    public string? CustomName { get; private set; }

    public CoffeeDrinkDefinition? DrinkDefinition { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ShopMenuItem()
    {
    }

    public static ShopMenuItem Create(
        Guid drinkDefinitionId,
        MenuItemAvailability availability,
        decimal? price,
        MenuItemSource source)
    {
        if (drinkDefinitionId == Guid.Empty)
            throw new DomainException("Drink definition is required.");
        if (!Enum.IsDefined(source))
            throw new DomainException("Menu item source is invalid.");
        ValidateValues(availability, price);

        return new ShopMenuItem
        {
            Id = Guid.NewGuid(),
            DrinkDefinitionId = drinkDefinitionId,
            Availability = availability,
            Price = price,
            Source = source,
            Kind = CoffeeDrinkKind.Standard
        };
    }

    public void ApplyParsed(decimal? price)
    {
        ValidateValues(MenuItemAvailability.Present, price);
        Availability = MenuItemAvailability.Present;
        Price = price;
    }

    public void ApplyManual(MenuItemAvailability availability, decimal? price)
    {
        ValidateValues(availability, price);
        Availability = availability;
        Price = price;
        Source = MenuItemSource.Manual;
    }

    internal void ApplySnapshot(ShopMenuItem item)
    {
        Availability = item.Availability;
        Price = item.Price;
        Source = item.Source;
    }

    public static void ValidateValues(MenuItemAvailability availability, decimal? price) =>
        CoffeePeek.Shared.Kernel.Menu.MenuItemValues.Validate((int)availability, price);
}
