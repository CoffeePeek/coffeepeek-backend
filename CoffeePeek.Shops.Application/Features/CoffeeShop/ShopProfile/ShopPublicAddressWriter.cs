using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using DomainShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

public static class ShopPublicAddressWriter
{
    public static void Apply(IPublicSlugOwner owner, string slug)
    {
        switch (owner)
        {
            case DomainShop shop: shop.SetPublicSlug(slug); break;
            case City city: city.SetPublicSlug(slug); break;
            case CoffeeZone zone: zone.SetPublicSlug(slug); break;
            case CoffeeBean bean: bean.SetPublicSlug(slug); break;
            case Equipment equipment: equipment.SetPublicSlug(slug); break;
            case BrewMethod method: method.SetPublicSlug(slug); break;
            case ShopTag tag: tag.SetPublicSlug(slug); break;
            default: throw new PublicAddressException("Unknown address owner.", 400);
        }
    }
}
