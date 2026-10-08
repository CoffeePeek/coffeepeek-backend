using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Menu;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Contract.Events;
using Wolverine;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Options;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Menu;

public static class AttachAdminShopMenuPhotosHandler
{
    public static async Task<(Response<AdminShopMenuDto>, ParseMenuOcrJobRequestedEvent?)> Handle(
        AttachAdminShopMenuPhotosCommand command,
        ICoffeeShopRepository shops,
        IApplyShopMenuService applyMenu,
        IQueryShopMenuRepository queryMenu,
        IQueryCoffeeDrinkRepository drinks,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        IUploadedPhotoLookup photoLookup,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return (Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found."), null);

        var menu = await applyMenu.GetOrCreateAsync(command.ShopId, ct);
        var now = DateTime.UtcNow;
        if (command.Photos.Count == 0)
            return (Response<AdminShopMenuDto>.Error(HttpStatusCode.BadRequest, "At least one menu photo is required."), null);
        var verified = await TrustedUploadedShopPhotos.ClaimAsync(
            photoLookup, command.UserId, command.ShopId,
            ShopPhotoAttachmentKind.Menu, command.Photos, ct);
        var inputPhotos = verified.Select(p =>
            ShopMenuPhoto.Create(p.FileName, p.ContentType, p.StorageKey, p.SizeBytes,
                p.PhotoId)).ToArray();
        var alreadyAttached = inputPhotos.All(photo => menu.Photos.Any(p =>
            p.StorageKey == photo.StorageKey && p.MediaPhotoId == photo.MediaPhotoId));
        if (alreadyAttached)
        {
            var existingCatalog = await drinks.GetActiveAsync(ct);
            var existing = ShopMenuDtoFactory.FromShopMenu(
                await queryMenu.GetByShopIdAsync(command.ShopId, ct), existingCatalog, mediaOptions.Value)!;
            return (Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(existing, [])), null);
        }
        menu.AddPhotos(inputPhotos);
        var inputKeys = inputPhotos.Select(p => p.StorageKey).ToHashSet(StringComparer.Ordinal);
        var job = menu.QueueOcrJob(menu.Photos.Where(p => inputKeys.Contains(p.StorageKey)).ToArray(), now);

        await bus.PublishAsync(new ShopPhotosAttachedEvent(command.UserId,
            command.ShopId, ShopPhotoAttachmentKind.Menu,
            verified.Select(p => p.StorageKey).ToArray()));
        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        var catalog = await drinks.GetActiveAsync(ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;

        return (
            Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, [])),
            new ParseMenuOcrJobRequestedEvent(job.Id, command.UserId));
    }
}
