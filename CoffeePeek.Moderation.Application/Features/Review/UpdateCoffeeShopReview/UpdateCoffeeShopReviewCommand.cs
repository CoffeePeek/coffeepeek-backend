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
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public bool ClearDrink { get; init; }
}
