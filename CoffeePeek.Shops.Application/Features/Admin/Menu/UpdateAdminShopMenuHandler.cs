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
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Options;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Menu;

public static class UpdateAdminShopMenuHandler
{
    public static async Task<Response<AdminShopMenuDto>> Handle(
        UpdateAdminShopMenuCommand command,
        ICoffeeShopRepository shops,
        IShopPriceRangeWriter priceRangeWriter,
        IApplyShopMenuService applyMenu,
        IQueryCoffeeDrinkRepository drinks,
        IQueryShopMenuRepository queryMenu,
        IUnitOfWork unitOfWork,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        if (command.Items is null || command.Items.Any(item => item is null))
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.BadRequest, "Menu items are required.");
        try
        {
            foreach (var item in command.Items)
                ShopMenuItem.ValidateValues((DomainAvailability)(int)item.Availability, item.Price);
        }
        catch (DomainException ex)
        {
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.BadRequest, ex.Message);
        }

        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found.");

        var catalog = await drinks.GetActiveAsync(ct);
        var bySlug = catalog.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);
        var updates = new List<ManualShopMenuItemUpdate>(command.Items.Count);
        var seenDrinkIds = new HashSet<Guid>();
        foreach (var item in command.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Slug) || !bySlug.TryGetValue(item.Slug.Trim(), out var drink))
                return Response<AdminShopMenuDto>.Error(
                    HttpStatusCode.BadRequest, $"Active coffee drink '{item.Slug}' was not found.");
            if (!seenDrinkIds.Add(drink.Id))
                return Response<AdminShopMenuDto>.Error(
                    HttpStatusCode.BadRequest, $"Coffee drink '{item.Slug}' is repeated in the menu update.");
            updates.Add(new ManualShopMenuItemUpdate(
                drink.Id,
                (DomainAvailability)(int)item.Availability,
                item.Price));
        }
        var menu = await applyMenu.ApplyManualItemsAsync(command.ShopId, updates, command.UserId, ct);

        if (command.ApplySuggestedPriceRange && menu.SuggestedPriceRange.HasValue)
        {
            priceRangeWriter.Apply(shop, (DomainPriceRange)(int)menu.SuggestedPriceRange.Value);
        }

        await unitOfWork.SaveChangesAsync(ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;
        return Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, []));
    }
}
