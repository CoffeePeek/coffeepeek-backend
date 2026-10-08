using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Persistance.Menu;

public static class MenuCrossModuleWriteFixture
{
    public static void ChangeShopsPrice(CoffeeShop shop) => shop.SetPriceRange(PriceRange.Moderate);
}
