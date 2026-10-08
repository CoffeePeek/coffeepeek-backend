using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Domain.Places;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Application.Services;

public class EnrichShopFromImportService(
    IQueryCoffeeShopRepository shopRepository,
    IUnitOfWork unitOfWork,
    ICacheService cacheService,
    ILogger<EnrichShopFromImportService> logger) : IEnrichShopFromImportService
{
    public async Task<int> EnrichShopsFromImportAsync(
        IReadOnlyList<ImportShopEnrichmentItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return 0;

        // Only shops near the dump (ShopPlaceMatcher never matches beyond 500 m) or referenced by id — this used to
        // load and track the whole Shops table.
        var shops = await shopRepository.ListForEnrichmentAsync(
            items.Where(i => i.ShopId.HasValue).Select(i => i.ShopId!.Value).Distinct().ToList(),
            items.Min(i => i.Latitude) - LatPad, items.Max(i => i.Latitude) + LatPad,
            items.Min(i => i.Longitude) - LonPad, items.Max(i => i.Longitude) + LonPad,
            ct);
        if (shops.Count == 0)
            return 0;

        var byId = shops.ToDictionary(s => s.Id);
        var enrichedIds = new HashSet<Guid>();

        foreach (var item in items)
        {
            var shop = ResolveShop(item, shops, byId);
            if (shop is null)
                continue;

            if (!shop.TryEnrichFromImport(item.Address, item.Instagram, item.Website, item.Phone))
                continue;

            if (enrichedIds.Add(shop.Id))
            {
                logger.LogInformation(
                    "Enriched published shop {ShopId} ({ShopName}) from import dump",
                    shop.Id,
                    shop.Name);
            }
        }

        if (enrichedIds.Count == 0)
            return 0;

        foreach (var shopId in enrichedIds)
            byId[shopId].AdvanceProfileRevision();

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var shopId in enrichedIds)
            await cacheService.RemoveAsync(CacheKey.Shop.Detail(shopId));

        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);

        return enrichedIds.Count;
    }

    // ≥ 500 m in both axes at any latitude where coffee shops exist.
    private const decimal LatPad = 0.005m;
    private const decimal LonPad = 0.02m;

    private static CoffeeShop? ResolveShop(
        ImportShopEnrichmentItem item,
        IReadOnlyList<CoffeeShop> shops,
        IReadOnlyDictionary<Guid, CoffeeShop> byId)
    {
        if (item.ShopId is { } shopId && byId.TryGetValue(shopId, out var known))
            return known;

        foreach (var shop in shops)
        {
            if (shop.Location?.Latitude is null || shop.Location.Longitude is null)
                continue;

            // Cheap distance pre-check before the name/phone normalization in IsSamePlace.
            // ponytail: still items × nearby shops; add a lat/lon grid if a single city grows past ~10k shops.
            if (Math.Abs(shop.Location.Latitude.Value - item.Latitude) > LatPad
                || Math.Abs(shop.Location.Longitude.Value - item.Longitude) > LonPad)
                continue;

            if (ShopPlaceMatcher.IsSamePlace(
                    shop.Name,
                    shop.Location.Latitude.Value,
                    shop.Location.Longitude.Value,
                    item.Name,
                    item.Latitude,
                    item.Longitude,
                    shop.Contact?.PhoneNumber,
                    item.Phone,
                    shop.Contact?.InstagramLink,
                    item.Instagram))
                return shop;
        }

        return null;
    }
}
