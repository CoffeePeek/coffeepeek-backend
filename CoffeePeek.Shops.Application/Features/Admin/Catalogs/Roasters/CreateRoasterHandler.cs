using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

public static class CreateRoasterHandler
{
    public static async Task<Response<RoasterDto>> Handle(
        CreateRoasterCommand command,
        IRoasterRepository repository,
        IMapper mapper,
        IUploadedPhotoLookup photoLookup,
        CancellationToken ct)
    {
        var existing = await repository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            return Response<RoasterDto>.Error(HttpStatusCode.Conflict, "A roaster with this name already exists.");

        var trustedPhotos = command.Photos is { Count: > 0 }
            ? await TrustedUploadedShopPhotos.ResolveAsync(photoLookup, command.ActorUserId, command.Photos, ct)
            : [];

        var roaster = new Roaster(command.Name);
        roaster.SetAbout(command.About);
        roaster.SetLocation(RoasterProfileFields.BuildLocation(command.CityId, command.Address, command.Latitude, command.Longitude));
        roaster.SetContact(RoasterProfileFields.BuildContact(command.InstagramLink, command.SiteLink));
        if (command.Photos is not null)
            roaster.ReplacePhotos(RoasterProfileFields.ToRoasterPhotos(trustedPhotos, command.ActorUserId));

        repository.Add(roaster);

        return Response<RoasterDto>.Success(mapper.Map<RoasterDto>(roaster));
    }
}
