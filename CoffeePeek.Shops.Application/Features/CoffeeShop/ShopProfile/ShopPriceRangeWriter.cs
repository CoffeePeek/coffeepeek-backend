using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

public sealed class ShopPriceRangeWriter : IShopPriceRangeWriter
{
    public void Apply(DomainCoffeeShop shop, PriceRange range)
    {
        if (shop.PriceRange == range) return;
        shop.SetPriceRange(range);
        shop.AdvanceProfileRevision();
    }
}
