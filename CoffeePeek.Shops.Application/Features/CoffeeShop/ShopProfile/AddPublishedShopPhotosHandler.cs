using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Contract.Events;
using Wolverine;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Microsoft.Extensions.Options;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

/// <summary>Adds photos to a published shop gallery and returns the updated shop.</summary>
public static class AddPublishedShopPhotosHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        AddPublishedShopPhotosCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        IUploadedPhotoLookup photoLookup,
        IMessageBus bus,
        CancellationToken ct)
    {
        if (command.Photos is null || command.Photos.Count == 0)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest, "At least one photo is required.");

        var shop = await LoadShopAsync(repository, command.ShopId, command.OwnerUserId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        var verified = await TrustedUploadedShopPhotos.ClaimAsync(
            photoLookup, command.ActorUserId, command.ShopId,
            ShopPhotoAttachmentKind.Gallery, command.Photos, ct);
        var photos = verified.Select(p => new ShopPhoto(
            p.FileName,
            p.ContentType,
            p.StorageKey,
            p.SizeBytes,
            command.ActorUserId, id: p.PhotoId)).ToList();

        var attached = await repository.TryAttachGalleryPhotosAsync(
            command.ShopId, command.OwnerUserId, photos, ct);
        if (!attached)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        shop.AdvanceProfileRevision();

        await bus.PublishAsync(new ShopPhotosAttachedEvent(command.ActorUserId,
            command.ShopId, ShopPhotoAttachmentKind.Gallery,
            verified.Select(p => p.StorageKey).ToArray()));
        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(command.ShopId));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        shop = await LoadShopAsync(repository, command.ShopId, command.OwnerUserId, ct);
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
