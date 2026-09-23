using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ModerationRoasterDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? About { get; init; }
    public ModerationStatus ModerationStatus { get; init; }
    public Guid? CityId { get; init; }
    public LocationDto? Location { get; init; }
    public RoasterContactDto? Contact { get; init; }
    public PhotoMetadataDto[] Photos { get; init; } = [];
}
