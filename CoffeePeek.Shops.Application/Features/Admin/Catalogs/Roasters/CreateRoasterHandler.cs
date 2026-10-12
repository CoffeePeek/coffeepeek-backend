using System.Net;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Contract.Events;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

public static class CreateRoasterHandler
{
    public static async Task<Response<RoasterDto>> Handle(
        CreateRoasterCommand command,
        IRoasterRepository repository,
        IMapper mapper,
        IUploadedPhotoLookup photoLookup,
        IMessageBus bus,
        CancellationToken ct)
    {
        var existing = await repository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            return Response<RoasterDto>.Error(HttpStatusCode.Conflict, "A roaster with this name already exists.");

        var keys = command.Photos is { Count: > 0 }
            ? TrustedUploadedShopPhotos.ValidateKeys(command.ActorUserId, command.Photos) : [];
        var roaster = new Roaster(command.Name, id: RoasterProfileFields.CreateId(command.ActorUserId, keys));
        if (keys.Length > 0 && await repository.GetByIdAsync(roaster.Id, ct) != null)
            return Response<RoasterDto>.Error(HttpStatusCode.Conflict, "These uploads already belong to a roaster.");
        roaster.SetAbout(command.About);
        roaster.SetLocation(RoasterProfileFields.BuildLocation(command.CityId, command.Address, command.Latitude, command.Longitude));
        roaster.SetContact(RoasterProfileFields.BuildContact(command.InstagramLink, command.SiteLink));
        var attached = command.Photos is null ? [] : await RoasterProfileFields.ReplacePhotosAsync(
            roaster, command.ActorUserId, command.Photos, photoLookup, ct);

        repository.Add(roaster);
        if (attached.Length > 0) await bus.PublishAsync(new ShopPhotosAttachedEvent(command.ActorUserId,
            roaster.Id, ShopPhotoAttachmentKind.Roaster, attached));

        return Response<RoasterDto>.Success(mapper.Map<RoasterDto>(roaster));
    }
}
