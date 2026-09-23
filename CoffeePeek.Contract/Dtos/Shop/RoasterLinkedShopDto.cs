namespace CoffeePeek.Contract.Dtos.Shop;

public class RoasterLinkedShopDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public ShortPhotoMetadataDto? CoverPhoto { get; init; }
}
