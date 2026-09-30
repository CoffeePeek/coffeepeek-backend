using CoffeePeek.Shared.Domain.PublicAddresses;

namespace CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;

public sealed class RoasterPublicAddress : PublicAddress
{
    private RoasterPublicAddress() { }
    private RoasterPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static RoasterPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}
