using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Enums;
namespace CoffeePeek.ShopsService.Controllers;
public record PublicCreateUnifiedCheckInRequest([Required, MaxLength(100)] string CoffeeShopSlug,
    [MaxLength(1000)] string? Text = null, RatingDto? Rating = null,
    CheckInVisibility Visibility = CheckInVisibility.Private, string? DrinkSlug = null,
    string? CustomDrinkName = null, UploadedPhotoDto[]? Photos = null,
    [Required] [property: JsonRequired] DateTimeOffset? VisitedAt = null);
