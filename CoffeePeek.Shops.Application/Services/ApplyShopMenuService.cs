using System.Text.Json;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainSource = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemSource;
using Wolverine;

namespace CoffeePeek.Shops.Application.Services;

public class ApplyShopMenuService(
    IShopMenuRepository menuRepository,
    IQueryCoffeeDrinkRepository drinkRepository,
    ICoffeeShopRepository shopRepository,
    IShopPriceRangeWriter priceRangeWriter,
    IMessageBus bus) : IApplyShopMenuService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<ShopMenu> ApplyManualItemsAsync(
        Guid shopId,
        IReadOnlyList<ManualShopMenuItemUpdate> items,
        Guid? userId,
        CancellationToken ct)
    {
        var menu = await menuRepository.ApplyManualItemsAsync(shopId, items, userId, ct);
        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shopId));
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
        var suggested = snapshot.SuggestedPriceRange is null
            ? (DomainPriceRange?)null
            : (DomainPriceRange)(int)snapshot.SuggestedPriceRange.Value;

        // Resolve and validate the complete snapshot before changing the tracked menu.
        var items = new List<ShopMenuItem>();
        foreach (var row in snapshot.Items)
        {
            // Older OCR snapshots may contain partial offers. The new menu contract
            // skips those offers; invalid manual rows remain a validation failure.
            if (row.Source == Contract.Enums.MenuItemSource.Parsed &&
                (row.Availability != Contract.Enums.MenuItemAvailability.Present ||
                 row.Price is not > 0))
                continue;
            if (!bySlug.TryGetValue(row.Slug, out var drink))
                throw new CoffeePeek.Shared.Kernel.Exceptions.DomainException($"Coffee drink '{row.Slug}' was not found.");
            items.Add(ShopMenuItem.Create(
                drink.Id,
                (DomainAvailability)(int)row.Availability,
                row.Price,
                (DomainSource)(int)row.Source));
        }

        var menu = await GetOrCreateAsync(shopId, ct);
        // Every accepted snapshot advances the concurrency token, including empty and parsed-only snapshots.
        menu.MarkManuallyCaptured(snapshot.CapturedAtUtc ?? DateTime.UtcNow);
        if (snapshot.CapturedAtUtc.HasValue)
            menu.MarkParsePending(snapshot.CapturedAtUtc.Value);

        if (items.Count > 0)
        {
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

        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shopId));
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
            if (row.Price is not > 0)
                continue;
            mapped.Add(ShopMenuItem.Create(
                drink.Id,
                DomainAvailability.Present,
                row.Price,
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

        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shopId));
    }

    public async Task MarkParseFailedAsync(Guid shopId, string error, CancellationToken ct)
    {
        var menu = await GetOrCreateAsync(shopId, ct);
        menu.MarkParseFailed(error);
        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shopId));
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
        if (shop is not null) priceRangeWriter.Apply(shop, range);
    }
}
