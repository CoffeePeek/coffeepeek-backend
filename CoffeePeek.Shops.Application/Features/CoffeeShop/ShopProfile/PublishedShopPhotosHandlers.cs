using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Microsoft.Extensions.Options;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

/// <summary>
/// Attaches already-uploaded gallery photos. <see cref="OwnerUserId"/> is null for admin
/// and the owning user id for owner routes.
/// </summary>
public record AddPublishedShopPhotosCommand(
    Guid ShopId,
    Guid ActorUserId,
    Guid? OwnerUserId,
    IReadOnlyList<UploadedPhotoDto> Photos);

/// <summary>Adds photos to a published shop gallery and returns the updated shop.</summary>
public static class AddPublishedShopPhotosHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        AddPublishedShopPhotosCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        if (command.Photos is null || command.Photos.Count == 0)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest, "At least one photo is required.");

        var photos = command.Photos.Select(p => new ShopPhoto(
            p.FileName,
            p.ContentType,
            p.StorageKey,
            p.Size,
            command.ActorUserId)).ToList();

        var attached = await repository.TryAttachGalleryPhotosAsync(
            command.ShopId, command.OwnerUserId, photos, ct);
        if (!attached)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(command.ShopId));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        var shop = await LoadShopAsync(repository, command.ShopId, command.OwnerUserId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }

    internal static Task<DomainCoffeeShop?> LoadShopAsync(
        ICoffeeShopRepository repository,
        Guid shopId,
        Guid? ownerUserId,
        CancellationToken ct) =>
        ownerUserId is null
            ? repository.GetByIdAsync(shopId, ct)
            : repository.GetByIdForOwnerAsync(shopId, ownerUserId.Value, ct);
}

/// <summary>Removes gallery photos by id. Remaining photos are reindexed from 0.</summary>
public record RemovePublishedShopPhotosCommand(
    Guid ShopId,
    Guid? OwnerUserId,
    IReadOnlyList<Guid> PhotoIds);

/// <summary>Deletes gallery photos on a published shop and returns the updated shop.</summary>
public static class RemovePublishedShopPhotosHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        RemovePublishedShopPhotosCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await AddPublishedShopPhotosHandler.LoadShopAsync(
            repository, command.ShopId, command.OwnerUserId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        var removed = shop.RemovePhotos(command.PhotoIds ?? []);
        if (removed.IsFailed)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                removed.Errors[0].Message);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
