using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Moderation.Domain;

namespace CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;

public record SendReviewToModerationCommand(
    Guid ShopId,
    [MaxLength(BusinessConstants.MaxReviewHeaderLength)]
    string? Header,
    [MaxLength(BusinessConstants.MaxReviewCommentLength)]
    string Comment,
    RatingDto? Rating,
    ICollection<UploadedPhotoDto>? Photos)
{
    [JsonIgnore] public Guid UserId { get; init; }
    [JsonIgnore] public string UserName { get; init; } = string.Empty;
    [JsonIgnore] public Guid? CheckInId { get; init; }

    [JsonPropertyName("ratingCoffee")] public int? RatingCoffee { get; init; }
    [JsonPropertyName("ratingPlace")] public int? RatingPlace { get; init; }
    [JsonPropertyName("ratingService")] public int? RatingService { get; init; }
    
    [JsonIgnore]
    public RatingDto EffectiveRating => Rating ?? new RatingDto
    {
        Coffee = RatingCoffee ?? 0,
        Place = RatingPlace ?? 0,
        Service = RatingService ?? 0
    };
}
