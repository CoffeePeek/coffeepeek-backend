using CoffeePeek.Shared.Domain.Entities;

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
        Availability = MenuItemAvailability.Present;
        Price = price;
        VolumeMl = volumeMl;
    }

    public void ApplyManual(MenuItemAvailability availability, decimal? price, int? volumeMl)
    {
        Availability = availability;
        Price = availability == MenuItemAvailability.Present ? price : null;
        VolumeMl = volumeMl;
        Source = MenuItemSource.Manual;
    }
}
