using CoffeePeek.Shared.Domain.PublicAddresses;

namespace CoffeePeek.Account.Domain.Entities.PublicAddressAggregate;

public sealed class UserPublicAddress : PublicAddress
{
    private UserPublicAddress() { }
    private UserPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static UserPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}
