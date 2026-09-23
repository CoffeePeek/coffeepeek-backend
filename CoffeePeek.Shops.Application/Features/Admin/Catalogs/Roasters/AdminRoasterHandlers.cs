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

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

public record CreateRoasterCommand(
    string Name,
    string? About = null,
    Guid? CityId = null,
    string? Address = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? InstagramLink = null,
    string? SiteLink = null,
    List<UploadedPhotoDto>? Photos = null,
    [property: JsonIgnore] Guid ActorUserId = default);

public static class CreateRoasterHandler
{
    public static async Task<Response<RoasterDto>> Handle(
        CreateRoasterCommand command,
        IRoasterRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var existing = await repository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            return Response<RoasterDto>.Error(HttpStatusCode.Conflict, "A roaster with this name already exists.");

        var roaster = new Roaster(command.Name);
        roaster.SetAbout(command.About);
        roaster.SetLocation(RoasterProfileFields.BuildLocation(command.CityId, command.Address, command.Latitude, command.Longitude));
        roaster.SetContact(RoasterProfileFields.BuildContact(command.InstagramLink, command.SiteLink));
        if (command.Photos is not null)
            roaster.ReplacePhotos(RoasterProfileFields.ToRoasterPhotos(command.Photos, command.ActorUserId));

        repository.Add(roaster);
        await unitOfWork.SaveChangesAsync(ct);
        await InvalidateRoasterCachesAsync(cacheService, ct);

        return Response<RoasterDto>.Success(mapper.Map<RoasterDto>(roaster));
    }

    internal static async Task InvalidateRoasterCachesAsync(ICacheService cacheService, CancellationToken ct)
    {
        await cacheService.RemoveByPattern(CacheKey.Roaster.ListPattern(), ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }
}

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
    [property: JsonIgnore] Guid ActorUserId = default);

public static class UpdateRoasterHandler
{
    public static async Task<Response<RoasterDto>> Handle(
        UpdateRoasterCommand command,
        IRoasterRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var roaster = await repository.GetByIdAsync(command.Id, ct);
        if (roaster is null)
            return Response<RoasterDto>.Error(HttpStatusCode.NotFound, "Roaster not found.");

        roaster.Update(command.Name, command.About);
        roaster.SetLocation(RoasterProfileFields.BuildLocation(command.CityId, command.Address, command.Latitude, command.Longitude));
        roaster.SetContact(RoasterProfileFields.BuildContact(command.InstagramLink, command.SiteLink));
        if (command.Photos is not null)
            roaster.ReplacePhotos(RoasterProfileFields.ToRoasterPhotos(command.Photos, command.ActorUserId));

        await unitOfWork.SaveChangesAsync(ct);
        await CreateRoasterHandler.InvalidateRoasterCachesAsync(cacheService, ct);

        return Response<RoasterDto>.Success(mapper.Map<RoasterDto>(roaster));
    }
}

internal static class RoasterProfileFields
{
    public static Location? BuildLocation(Guid? cityId, string? address, decimal? latitude, decimal? longitude)
    {
        if (cityId is null || string.IsNullOrWhiteSpace(address))
            return null;

        return latitude.HasValue && longitude.HasValue
            ? Location.CreateValidated(cityId.Value, address, latitude.Value, longitude.Value)
            : Location.CreateDraft(cityId.Value, address);
    }

    public static RoasterContact? BuildContact(string? instagramLink, string? siteLink)
    {
        if (string.IsNullOrWhiteSpace(instagramLink) && string.IsNullOrWhiteSpace(siteLink))
            return null;

        return RoasterContact.Create(instagramLink, siteLink);
    }

    public static IEnumerable<RoasterPhoto> ToRoasterPhotos(IEnumerable<UploadedPhotoDto> photos, Guid ownerId) =>
        photos.Select(p => new RoasterPhoto(p.FileName, p.ContentType, p.StorageKey, p.Size, ownerId));
}

public record DeleteRoasterCommand([property: JsonIgnore] Guid Id);

public static class DeleteRoasterHandler
{
    public static async Task<Response> Handle(
        DeleteRoasterCommand command,
        IRoasterRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var roaster = await repository.GetByIdAsync(command.Id, ct);
        if (roaster is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Roaster not found.");

        repository.Remove(roaster);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateRoasterHandler.InvalidateRoasterCachesAsync(cacheService, ct);

        return Response.Success(message: "Roaster deleted.");
    }
}
