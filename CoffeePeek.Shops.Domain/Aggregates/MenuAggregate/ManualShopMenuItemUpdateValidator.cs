using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public static class ManualShopMenuItemUpdateValidator
{
    public static void Validate(IReadOnlyList<ManualShopMenuItemUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var seen = new HashSet<Guid>();
        foreach (var update in updates)
        {
            if (update.DrinkDefinitionId == Guid.Empty)
                throw new DomainException("Drink definition is required.");
            if (!seen.Add(update.DrinkDefinitionId))
                throw new DomainException("Menu update contains a repeated coffee drink.");
            ShopMenuItem.ValidateValues(update.Availability, update.Price, update.VolumeMl);
        }
    }
}
