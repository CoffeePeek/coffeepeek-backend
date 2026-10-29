namespace CoffeePeek.Contract.Dtos.Shop;

public record RoasterDetailsDto
{
    public int CoffeeShopsCount { get; init; }
    public int CoffeeProductsCount { get; init; }
    public Guid Id { get; init; }
    public long ProfileRevision { get; init; }
    public required string Name { get; init; }
    public Guid? CityId { get; init; }
    public string? About { get; init; }
    public LocationDto? Location { get; init; }
    public RoasterContactDto? Contact { get; init; }
    public ShortPhotoMetadataDto[] Photos { get; init; } = [];
    public RoasterLinkedShopDto[] Shops { get; init; } = [];
}
