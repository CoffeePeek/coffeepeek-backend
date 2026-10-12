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

public static class UpdateRoasterHandler
{
    public static async Task<Response<RoasterDto>> Handle(
        UpdateRoasterCommand command,
        IRoasterRepository repository,
        IMapper mapper,
        IUploadedPhotoLookup photoLookup,
        IMessageBus bus,
        CancellationToken ct)
    {
        var roaster = await repository.GetByIdAsync(command.Id, ct);
        if (roaster is null)
            return Response<RoasterDto>.Error(HttpStatusCode.NotFound, "Roaster not found.");

        Roaster.ValidateDetails(command.Name, command.About);
        var location = RoasterProfileFields.BuildLocation(command.CityId, command.Address, command.Latitude, command.Longitude);
        var contact = RoasterProfileFields.BuildContact(command.InstagramLink, command.SiteLink);
        var attached = command.Photos is null ? [] : await RoasterProfileFields.ReplacePhotosAsync(
            roaster, command.ActorUserId, command.Photos, photoLookup, ct);

        roaster.Update(command.Name, command.About);
        roaster.SetLocation(location);
        roaster.SetContact(contact);
        if (attached.Length > 0) await bus.PublishAsync(new ShopPhotosAttachedEvent(command.ActorUserId,
            roaster.Id, ShopPhotoAttachmentKind.Roaster, attached));

        return Response<RoasterDto>.Success(mapper.Map<RoasterDto>(roaster));
    }
}
