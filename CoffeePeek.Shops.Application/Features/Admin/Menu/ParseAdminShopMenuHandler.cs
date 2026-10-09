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
using Wolverine;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Options;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Menu;

public static class ParseAdminShopMenuHandler
{
    public static async Task<(Response<AdminShopMenuDto>, ParseMenuOcrJobRequestedEvent?)> Handle(
        ParseAdminShopMenuCommand command,
        ICoffeeShopRepository shops,
        IShopMenuRepository menuRepository,
        IQueryShopMenuRepository queryMenu,
        IQueryCoffeeDrinkRepository drinks,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return (Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found."), null);

        var menu = await menuRepository.GetTrackedByShopIdAsync(command.ShopId, ct);
        if (menu is null || menu.Photos.Count == 0)
            return (Response<AdminShopMenuDto>.Error(HttpStatusCode.BadRequest, "Attach menu photos before parsing."), null);

        var job = menu.QueueOcrJob(menu.Photos.ToArray(), DateTime.UtcNow);
        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        await unitOfWork.SaveChangesAsync(ct);

        var catalog = await drinks.GetActiveAsync(ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;

        return (
            Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, [])),
            new ParseMenuOcrJobRequestedEvent(job.Id, command.UserId));
    }
}
