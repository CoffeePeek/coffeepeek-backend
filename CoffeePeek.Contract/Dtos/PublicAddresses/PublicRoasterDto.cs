namespace CoffeePeek.Contract.Dtos.PublicAddresses;

/// <summary>Complete public roaster list item; product counts exclude drafts and archived coffee.</summary>
public sealed record PublicRoasterDto(PublicAddressDto? Address, string Name, string? PhotoUrl,
    ShortPhotoMetadataDto? CoverPhoto, PublicTagDto[] Tags, int CoffeeShopsCount, int CoffeeProductsCount,
    int AvailableCoffeeProducts);
