using CoffeePeek.Contract.Enums;
namespace CoffeePeek.Contract.Dtos.CheckIns;
public record UpdateUnifiedCheckInRequest(string Text, RatingDto Rating, string? DrinkSlug = null, string? CustomDrinkName = null);
