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
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Options;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Menu;

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
