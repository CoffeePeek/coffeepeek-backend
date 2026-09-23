using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Moderation.Application.Features.Roaster.SubmitRoaster;

public record SubmitRoasterCommand
{
    [JsonIgnore] public Guid? UserId { get; init; }

    [Required, MaxLength(100)] public string Name { get; init; }

    public string? About { get; init; }
    public Guid? CityId { get; init; }
    public string? Address { get; init; }
    public string? InstagramLink { get; init; }
    public string? SiteLink { get; init; }
    public List<UploadedPhotoDto>? Photos { get; init; }
}
