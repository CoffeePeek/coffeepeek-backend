using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed class ShopMenuItem : Entity<Guid>
{
    public Guid ShopMenuId { get; private set; }
    public Guid DrinkDefinitionId { get; private set; }
    public MenuItemAvailability Availability { get; private set; }
    public decimal? Price { get; private set; }
    public int? VolumeMl { get; private set; }
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
        int? volumeMl,
        MenuItemSource source)
    {
        if (drinkDefinitionId == Guid.Empty)
            throw new DomainException("Drink definition is required.");
        if (!Enum.IsDefined(source))
            throw new DomainException("Menu item source is invalid.");
        ValidateValues(availability, price, volumeMl);

        return new ShopMenuItem
        {
            Id = Guid.NewGuid(),
            DrinkDefinitionId = drinkDefinitionId,
            Availability = availability,
            Price = price,
            VolumeMl = volumeMl,
            Source = source,
            Kind = CoffeeDrinkKind.Standard
        };
    }

    public void ApplyParsed(decimal? price, int? volumeMl)
    {
        ValidateValues(MenuItemAvailability.Present, price, volumeMl);
        Availability = MenuItemAvailability.Present;
        Price = price;
        VolumeMl = volumeMl;
    }

    public void ApplyManual(MenuItemAvailability availability, decimal? price, int? volumeMl)
    {
        ValidateValues(availability, price, volumeMl);
        Availability = availability;
        Price = price;
        VolumeMl = volumeMl;
        Source = MenuItemSource.Manual;
    }

    internal void ApplySnapshot(ShopMenuItem item)
    {
        Availability = item.Availability;
        Price = item.Price;
        VolumeMl = item.VolumeMl;
        Source = item.Source;
    }

    public static void ValidateValues(MenuItemAvailability availability, decimal? price, int? volumeMl)
    {
        if (!Enum.IsDefined(availability))
            throw new DomainException("Menu item availability is invalid.");

        if (availability == MenuItemAvailability.Present)
        {
            if (price is null or <= 0)
                throw new DomainException("Present menu item requires a positive price.");
            if (volumeMl is null or <= 0)
                throw new DomainException("Present menu item requires a positive volume.");
        }
        else if (price is not null || volumeMl is not null)
        {
            throw new DomainException("Absent or unknown menu item must not have a price or volume.");
        }
    }
}
