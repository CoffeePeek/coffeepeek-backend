using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public sealed class PublicAddressOwnerWriter : IPublicAddressOwnerWriter
{
    public void Apply(IPublicSlugOwner owner, string slug, long? expectedRevision = null)
    {
        if (expectedRevision.HasValue && owner.SlugRevision != expectedRevision.Value)
            throw new PublicAddressException("Expected revision does not match the current address.", 409);
        if (owner is CoffeeProduct or Roaster) CoffeeCatalogPublicAddressWriter.Apply(owner, slug);
        else ShopPublicAddressWriter.Apply(owner, slug);
    }
}
