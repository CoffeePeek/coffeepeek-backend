using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shops.Domain;

namespace CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;

public record CreateCheckInCommand(
    [Required] Guid CoffeeShopId,
    [Required] bool IsPublic,
    [Required] DateTime VisitedAt,
    [MaxLength(BusinessConstants.MaxCheckInNoteLength)]
    string? Note = null,
    ICollection<UploadedPhotoDto>? Photos = null,
    [Required] RatingDto? Rating = null,
    [MaxLength(BusinessConstants.MaxReviewHeaderLength)] string? Header = null)
{
    [JsonIgnore] public Guid UserId { get; init; }
    [JsonIgnore] public string UserName { get; init; } = string.Empty;
}