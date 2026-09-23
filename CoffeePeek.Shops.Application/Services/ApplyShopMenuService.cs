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

public class ApplyShopMenuService(
    IShopMenuRepository menuRepository,
    IQueryCoffeeDrinkRepository drinkRepository,
    ICoffeeShopRepository shopRepository,
    ICacheService cacheService) : IApplyShopMenuService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<ShopMenu> ApplyManualItemsAsync(
        Guid shopId,
        IReadOnlyList<ManualShopMenuItemUpdate> items,
        Guid? userId,
        CancellationToken ct)
    {
        var menu = await menuRepository.ApplyManualItemsAsync(shopId, items, userId, ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
        return menu;
    }

    public async Task ApplySnapshotAsync(
        Guid shopId,
        ShopMenuSnapshot snapshot,
        bool applySuggestedPriceRange,
        Guid? userId,
        CancellationToken ct)
    {
        var catalog = await drinkRepository.GetActiveAsync(ct);
        var bySlug = catalog.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);
        var menu = await GetOrCreateAsync(shopId, ct);

        if (snapshot.CapturedAtUtc.HasValue)
            menu.MarkParsePending(snapshot.CapturedAtUtc.Value);

        var suggested = snapshot.SuggestedPriceRange is null
            ? (DomainPriceRange?)null
            : (DomainPriceRange)(int)snapshot.SuggestedPriceRange.Value;

        if (snapshot.Items.Count > 0)
        {
            var items = new List<ShopMenuItem>();
            foreach (var row in snapshot.Items)
            {
                if (!bySlug.TryGetValue(row.Slug, out var drink))
                    continue;
                items.Add(ShopMenuItem.Create(
                    drink.Id,
                    (DomainAvailability)(int)row.Availability,
                    row.Price,
                    row.VolumeMl,
                    (DomainSource)(int)row.Source));
            }

            menu.ApplyParsedItems(items, snapshot.UnmatchedJson, suggested, userId);
        }
        else if (snapshot.CapturedAtUtc.HasValue)
        {
            menu.CompleteParse(snapshot.UnmatchedJson, suggested, userId);
        }

        if (snapshot.Photos.Count > 0)
        {
            menu.ReplacePhotos(snapshot.Photos.Select(p => ShopMenuPhoto.Create(
                p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.MediaPhotoId)));
        }

        if (applySuggestedPriceRange && suggested.HasValue)
            await ApplyPriceRangeAsync(shopId, suggested.Value, ct);

        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shopId));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }

    public async Task ApplyParseResultAsync(
        Guid shopId,
        IReadOnlyList<ParsedMenuItemDto> items,
        IReadOnlyList<UnmatchedMenuItemDto> unmatched,
        Contract.Enums.PriceRange? suggestedPriceRange,
        IReadOnlyList<ShopMenuPhotoSnapshot> photos,
        DateTime capturedAtUtc,
        Guid? userId,
        CancellationToken ct)
    {
        var catalog = await drinkRepository.GetActiveAsync(ct);
        var bySlug = catalog.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);
        var menu = await GetOrCreateAsync(shopId, ct);
        menu.MarkParsePending(capturedAtUtc);

        var mapped = new List<ShopMenuItem>();
        foreach (var row in items)
        {
            if (!bySlug.TryGetValue(row.Slug, out var drink))
                continue;
            mapped.Add(ShopMenuItem.Create(
                drink.Id,
                DomainAvailability.Present,
                row.Price,
                row.VolumeMl,
                DomainSource.Parsed));
        }

        var suggested = suggestedPriceRange is null
            ? (DomainPriceRange?)null
            : (DomainPriceRange)(int)suggestedPriceRange.Value;

        menu.MergeParsedItems(mapped, JsonSerializer.Serialize(unmatched, JsonOpts), suggested, userId);
        // Photos accumulate: a parse request carries only the newly attached photos.
        if (photos.Count > 0)
            menu.AddPhotos(photos.Select(p => ShopMenuPhoto.Create(
                p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.MediaPhotoId)));

        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shopId));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }

    public async Task MarkParseFailedAsync(Guid shopId, string error, CancellationToken ct)
    {
        var menu = await GetOrCreateAsync(shopId, ct);
        menu.MarkParseFailed(error);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shopId));
    }

    public async Task<ShopMenu> GetOrCreateAsync(Guid shopId, CancellationToken ct)
    {
        var existing = await menuRepository.GetTrackedByShopIdAsync(shopId, ct);
        if (existing is not null)
            return existing;

        var created = ShopMenu.Create(shopId);
        menuRepository.Add(created);
        return created;
    }

    private async Task ApplyPriceRangeAsync(Guid shopId, DomainPriceRange range, CancellationToken ct)
    {
        var shop = await shopRepository.GetByIdAsync(shopId, ct);
        shop?.SetPriceRange(range);
    }
}
