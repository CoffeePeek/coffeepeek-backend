using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

public static class UpdateRoasterHandler
{
    public static async Task<Response<RoasterDto>> Handle(
        UpdateRoasterCommand command,
        IRoasterRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IUploadedPhotoLookup photoLookup,
        CancellationToken ct)
    {
        var roaster = await repository.GetByIdAsync(command.Id, ct);
        if (roaster is null)
            return Response<RoasterDto>.Error(HttpStatusCode.NotFound, "Roaster not found.");

        var trustedPhotos = command.Photos is { Count: > 0 }
            ? await TrustedUploadedShopPhotos.ResolveAsync(photoLookup, command.ActorUserId, command.Photos, ct)
            : [];

        roaster.Update(command.Name, command.About);
        roaster.SetLocation(RoasterProfileFields.BuildLocation(command.CityId, command.Address, command.Latitude, command.Longitude));
        roaster.SetContact(RoasterProfileFields.BuildContact(command.InstagramLink, command.SiteLink));
        if (command.Photos is not null)
            roaster.ReplacePhotos(RoasterProfileFields.ToRoasterPhotos(trustedPhotos, command.ActorUserId));

        await unitOfWork.SaveChangesAsync(ct);
        await CreateRoasterHandler.InvalidateRoasterCachesAsync(cacheService, ct);

        return Response<RoasterDto>.Success(mapper.Map<RoasterDto>(roaster));
    }
}
