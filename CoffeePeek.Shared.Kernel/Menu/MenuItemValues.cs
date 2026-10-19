using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shared.Kernel.Menu;

public static class MenuItemValues
{
    public static void Validate(int availability, decimal? price)
    {
        if (availability is < 0 or > 2)
            throw new DomainException("Menu item availability is invalid.");
        if (availability == 1 && price is null or <= 0)
            throw new DomainException("Present menu item requires a positive price.");
        if (availability != 1 && price is not null)
            throw new DomainException("Absent or unknown menu item must not have a price.");
        if (price > 99_999_999.99m)
            throw new DomainException("Menu item price exceeds 99999999.99.");
    }
}
