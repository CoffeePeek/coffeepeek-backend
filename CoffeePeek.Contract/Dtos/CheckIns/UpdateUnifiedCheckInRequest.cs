using CoffeePeek.Contract.Enums;
namespace CoffeePeek.Contract.Dtos.CheckIns;
public record UpdateUnifiedCheckInRequest(string? Text = null, RatingDto? Rating = null, string? DrinkSlug = null, string? CustomDrinkName = null);
