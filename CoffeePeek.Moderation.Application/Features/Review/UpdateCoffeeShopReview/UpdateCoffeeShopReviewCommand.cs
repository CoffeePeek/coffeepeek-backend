using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;

public record UpdateCoffeeShopReviewCommand(
    [property:JsonIgnore]Guid UserId,
    Guid ReviewId,
    string? Header,
    string Comment,
    RatingDto Rating)
{
    /// <summary>Null retains current photos; an empty collection clears them.</summary>
    public List<UploadedPhotoDto>? Photos { get; init; }
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public bool ClearDrink { get; init; }
}
