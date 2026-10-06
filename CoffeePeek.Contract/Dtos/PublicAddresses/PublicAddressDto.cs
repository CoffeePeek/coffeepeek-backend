using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

// Public identities are addresses. These types deliberately have no aggregate GUIDs.
public sealed record PublicAddressDto(string Slug, string CanonicalPath, long Revision, bool IsAlias = false)
{
    public static PublicAddressDto From(PublicAddressMetadata address) =>
        new(address.Slug, address.CanonicalPath, address.Revision, address.IsAlias);
}
