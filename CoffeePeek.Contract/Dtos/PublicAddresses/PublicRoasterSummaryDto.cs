using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicRoasterSummaryDto(PublicAddressDto? Address, string Name, string? PhotoUrl, ShortPhotoMetadataDto? CoverPhoto)
{
    /// <summary>Visible shops using this roaster; null on references without statistics.</summary>
    public int? CoffeeShopsCount { get; init; }
    /// <summary>Published coffee products, counting each product once regardless of stock or packaging.</summary>
    public int? CoffeeProductsCount { get; init; }
}
