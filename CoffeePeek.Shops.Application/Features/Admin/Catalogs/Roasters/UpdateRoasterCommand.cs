using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

public record UpdateRoasterCommand(
    [property: JsonIgnore] Guid Id,
    string Name,
    string? About = null,
    Guid? CityId = null,
    string? Address = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? InstagramLink = null,
    string? SiteLink = null,
    List<UploadedPhotoDto>? Photos = null,
    [property: JsonIgnore] Guid ActorUserId = default)
{
    [JsonIgnore] public Guid? OwnerUserId { get; init; }
    [JsonIgnore] public long? ExpectedRevision { get; init; }
}
