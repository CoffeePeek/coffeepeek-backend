using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Enums;
namespace CoffeePeek.ShopsService.Controllers;
public record PublicCreateUnifiedCheckInRequest([Required, MaxLength(100)] string CoffeeShopSlug,
    [Required, MaxLength(1000)] string Text, [Required] RatingDto Rating,
    CheckInVisibility Visibility = CheckInVisibility.Private, string? DrinkSlug = null,
    string? CustomDrinkName = null, UploadedPhotoDto[]? Photos = null, DateTimeOffset? VisitedAt = null);
