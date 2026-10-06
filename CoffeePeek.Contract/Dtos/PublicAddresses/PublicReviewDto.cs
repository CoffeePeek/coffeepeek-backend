using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicReviewDto
{
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public string? DrinkNameRu { get; init; }
    public string? DrinkNameEn { get; init; }

    public Guid Id { get; init; }
    public PublicAddressDto? Author { get; init; }
    public PublicAddressDto? Shop { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? Header { get; init; }
    public string Comment { get; init; } = string.Empty;
    public RatingDto Rating { get; init; } = new();
    public Guid? ModerationReviewId { get; init; }
    public ICollection<UploadedPhotoDto> Photos { get; init; } = [];
    public DateTime CreatedAtUtc { get; init; }
    public int HelpfulCount { get; init; }
    public bool IsHelpfulByCurrentUser { get; init; }
}
