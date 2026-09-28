using CoffeePeek.Shared.Domain.PublicAddresses;

namespace CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;

public sealed class CoffeeZonePublicAddress : PublicAddress
{
    private CoffeeZonePublicAddress() { }
    private CoffeeZonePublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static CoffeeZonePublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}
