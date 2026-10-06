using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicCheckInDto
{
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public string? DrinkNameRu { get; init; }
    public string? DrinkNameEn { get; init; }

    public Guid Id { get; init; }
    public PublicAddressDto? Shop { get; init; }
    public string Note { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime VisitedAt { get; init; }
    public Guid? ReviewId { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public RatingDto Rating { get; init; } = new();
    public ShortPhotoMetadataDto[] Photos { get; init; } = [];
}
