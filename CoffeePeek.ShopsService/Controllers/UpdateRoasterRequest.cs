using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

namespace CoffeePeek.ShopsService.Controllers;

public record UpdateRoasterRequest(
    string Name,
    string? About = null,
    Guid? CityId = null,
    string? Address = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? InstagramLink = null,
    string? SiteLink = null,
    List<UploadedPhotoDto>? Photos = null)
{
    internal UpdateRoasterCommand ToCommand(Guid id, Guid actorId, long? revision, Guid? ownerId = null) =>
        new(id, Name, About, CityId, Address, Latitude, Longitude, InstagramLink, SiteLink, Photos, actorId)
        { ExpectedRevision = revision, OwnerUserId = ownerId };
}
