using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public static class CoffeeCatalogPublicAddressWriter
{
    public static void Apply(IPublicSlugOwner owner, string slug)
    {
        switch (owner)
        {
            case CoffeeProduct product: product.SetPublicSlug(slug); break;
            case Roaster roaster: roaster.SetPublicSlug(slug); break;
            default: throw new PublicAddressException("Unknown catalog address owner.", 400);
        }
    }
}
