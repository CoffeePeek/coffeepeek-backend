using System.Text.Json;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainSource = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemSource;

namespace CoffeePeek.Shops.Application.Services;

public interface IApplyShopMenuService
{
    Task<ShopMenu> ApplyManualItemsAsync(
        Guid shopId,
        IReadOnlyList<ManualShopMenuItemUpdate> items,
        Guid? userId,
        CancellationToken ct);

    Task ApplySnapshotAsync(
        Guid shopId,
        ShopMenuSnapshot snapshot,
        bool applySuggestedPriceRange,
        Guid? userId,
        CancellationToken ct);

    Task ApplyParseResultAsync(
        Guid shopId,
        IReadOnlyList<ParsedMenuItemDto> items,
        IReadOnlyList<UnmatchedMenuItemDto> unmatched,
        Contract.Enums.PriceRange? suggestedPriceRange,
        IReadOnlyList<ShopMenuPhotoSnapshot> photos,
        DateTime capturedAtUtc,
        Guid? userId,
        CancellationToken ct);

    Task MarkParseFailedAsync(Guid shopId, string error, CancellationToken ct);

    Task<ShopMenu> GetOrCreateAsync(Guid shopId, CancellationToken ct);
}
