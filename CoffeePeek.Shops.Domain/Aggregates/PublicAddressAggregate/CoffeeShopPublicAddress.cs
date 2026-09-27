using CoffeePeek.Shared.Domain.PublicAddresses;

namespace CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;

public sealed class CoffeeShopPublicAddress : PublicAddress
{
    private CoffeeShopPublicAddress() { }
    private CoffeeShopPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static CoffeeShopPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}
