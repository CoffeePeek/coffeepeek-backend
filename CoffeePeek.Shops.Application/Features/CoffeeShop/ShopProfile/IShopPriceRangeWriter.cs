using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

public interface IShopPriceRangeWriter
{
    void Apply(DomainCoffeeShop shop, PriceRange range);
}
