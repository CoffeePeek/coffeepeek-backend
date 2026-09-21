using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Menu;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Options;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Menu;

public record GetAdminShopMenuQuery(Guid ShopId);

public static class GetAdminShopMenuHandler
{
    public static async Task<Response<AdminShopMenuDto>> Handle(
        GetAdminShopMenuQuery query,
        IQueryShopMenuRepository menuRepository,
        IQueryCoffeeDrinkRepository drinks,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var catalog = await drinks.GetActiveAsync(ct);
        var menu = await menuRepository.GetByShopIdAsync(query.ShopId, ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(menu, catalog, mediaOptions.Value);
        if (dto is null)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Menu not found.");

        var unmatched = ParseUnmatched(menu?.UnmatchedJson);
        return Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, unmatched));
    }

    private static IReadOnlyList<UnmatchedMenuItemDto> ParseUnmatched(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<UnmatchedMenuItemDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public record AttachAdminShopMenuPhotosCommand(
    Guid ShopId,
    IReadOnlyList<UploadedPhotoDto> Photos,
    Guid UserId);

public static class AttachAdminShopMenuPhotosHandler
{
    public static async Task<(Response<AdminShopMenuDto>, ParseMenuRequestedEvent?)> Handle(
        AttachAdminShopMenuPhotosCommand command,
        ICoffeeShopRepository shops,
        IApplyShopMenuService applyMenu,
        IQueryShopMenuRepository queryMenu,
        IQueryCoffeeDrinkRepository drinks,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return (Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found."), null);

        var menu = await applyMenu.GetOrCreateAsync(command.ShopId, ct);
        var now = DateTime.UtcNow;
        menu.MarkParsePending(now);
        menu.AddPhotos(command.Photos.Select(p =>
            ShopMenuPhoto.Create(p.FileName, p.ContentType, p.StorageKey, p.Size)));

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        var catalog = await drinks.GetActiveAsync(ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;

        return (
            Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, [])),
            new ParseMenuRequestedEvent(
                MenuParseSourceKind.PublishedShop,
                command.ShopId,
                command.ShopId,
                command.Photos.Select(p => new MenuPhotoRef(
                    p.FileName, p.ContentType, p.StorageKey, p.Size)).ToArray(),
                command.UserId));
    }
}

public record ParseAdminShopMenuCommand(Guid ShopId, Guid UserId);

public static class ParseAdminShopMenuHandler
{
    public static async Task<(Response<AdminShopMenuDto>, ParseMenuRequestedEvent?)> Handle(
        ParseAdminShopMenuCommand command,
        ICoffeeShopRepository shops,
        IShopMenuRepository menuRepository,
        IQueryShopMenuRepository queryMenu,
        IQueryCoffeeDrinkRepository drinks,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return (Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found."), null);

        var menu = await menuRepository.GetTrackedByShopIdAsync(command.ShopId, ct);
        if (menu is null || menu.Photos.Count == 0)
            return (Response<AdminShopMenuDto>.Error(HttpStatusCode.BadRequest, "Attach menu photos before parsing."), null);

        menu.MarkParsePending(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));

        var catalog = await drinks.GetActiveAsync(ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;

        return (
            Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, [])),
            new ParseMenuRequestedEvent(
                MenuParseSourceKind.PublishedShop,
                command.ShopId,
                command.ShopId,
                menu.Photos.Select(p => new MenuPhotoRef(
                    p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.MediaPhotoId)).ToArray(),
                command.UserId));
    }
}

public record RemoveAdminShopMenuPhotosCommand(
    Guid ShopId,
    IReadOnlyList<Guid> PhotoIds);

public static class RemoveAdminShopMenuPhotosHandler
{
    public static async Task<Response<AdminShopMenuDto>> Handle(
        RemoveAdminShopMenuPhotosCommand command,
        ICoffeeShopRepository shops,
        IShopMenuRepository menuRepository,
        IQueryShopMenuRepository queryMenu,
        IQueryCoffeeDrinkRepository drinks,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found.");

        var menu = await menuRepository.GetTrackedByShopIdAsync(command.ShopId, ct);
        if (menu is null)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Menu not found.");

        var removed = menu.RemovePhotos(command.PhotoIds ?? []);
        if (removed.IsFailed)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.BadRequest, removed.Errors[0].Message);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        var catalog = await drinks.GetActiveAsync(ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;
        return Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, []));
    }
}

public record UpdateAdminShopMenuCommand(
    Guid ShopId,
    IReadOnlyList<UpdateShopMenuItemRequest> Items,
    bool ApplySuggestedPriceRange,
    Guid UserId);

public static class UpdateAdminShopMenuHandler
{
    public static async Task<Response<AdminShopMenuDto>> Handle(
        UpdateAdminShopMenuCommand command,
        ICoffeeShopRepository shops,
        IApplyShopMenuService applyMenu,
        IQueryCoffeeDrinkRepository drinks,
        IQueryShopMenuRepository queryMenu,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found.");

        var catalog = await drinks.GetActiveAsync(ct);
        var bySlug = catalog.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);
        var updates = command.Items
            .Where(item => bySlug.ContainsKey(item.Slug))
            .Select(item => new ManualShopMenuItemUpdate(
                bySlug[item.Slug].Id,
                (DomainAvailability)(int)item.Availability,
                item.Price,
                item.VolumeMl))
            .ToArray();
        var menu = await applyMenu.ApplyManualItemsAsync(command.ShopId, updates, command.UserId, ct);

        if (command.ApplySuggestedPriceRange && menu.SuggestedPriceRange.HasValue)
            shop.SetPriceRange((DomainPriceRange)(int)menu.SuggestedPriceRange.Value);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;
        return Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, []));
    }
}
