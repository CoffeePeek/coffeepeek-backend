using CoffeePeek.Shared.Domain.PublicAddresses;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public interface IPublicAddressOwnerWriter
{
    void Apply(IPublicSlugOwner owner, string slug, long? expectedRevision = null);
}
