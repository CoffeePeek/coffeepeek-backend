using CoffeePeek.Contract.Constants;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Application.Services;

public class CreateShopFromImportService(
    IQueryCoffeeShopRepository shopRepository,
    IQueryShopTagRepository tagRepository,
    IQueryCityRepository cityRepository,
    IApplyShopMenuService applyMenu,
    IUnitOfWork unitOfWork,
    ICacheService cacheService,
    ILogger<CreateShopFromImportService> logger) : ICreateShopFromImportService
{
    public async Task<Guid> CreateShopFromImportAsync(
        ImportCandidatePublishedItem item,
        CancellationToken cancellationToken = default)
    {
        var existingId = await shopRepository.GetIdByModerationId(item.CandidateId, cancellationToken);
        if (existingId.HasValue)
        {
            logger.LogInformation(
                "Shop {ShopId} already exists for import candidate {CandidateId}",
                existingId.Value,
                item.CandidateId);
            return existingId.Value;
        }

        var cityId = await ResolveCityId(item.CityId, cancellationToken);

        var priceRange = item.Menu?.SuggestedPriceRange is { } suggested
            ? (PriceRange)(int)suggested
            : PriceRange.Moderate;

        var shop = new CoffeeShop(
            item.CreatorId,
            item.Name,
            description: null,
            priceRange,
            item.CandidateId);

        shop.SetLocation(cityId, item.Address, item.Latitude, item.Longitude);
        shop.SetContact(item.Instagram, email: null, item.Website, FirstPhone(item.Phone));
        shop.SetCoffeeFocus((CoffeeFocusType)(int)item.Type);

        if (item.TemporarilyClosed)
            shop.SetStatus(CoffeeShopStatus.TemporarilyClosed);

        if (item.ImportedFromFile)
            shop.MarkImportedFromFile(DateTime.UtcNow);

        var tags = await tagRepository.GetActiveBySlugsAsync(item.TagSlugs, cancellationToken);
        if (tags.Length > 0)
            shop.SetTags(tags.Select(t => t.Id).ToArray(), item.CreatorId);

        shopRepository.Add(shop);
        if (item.Menu is not null)
        {
            await applyMenu.ApplySnapshotAsync(
                shop.Id,
                item.Menu,
                applySuggestedPriceRange: false,
                item.CreatorId,
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), cancellationToken);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, cancellationToken);

        logger.LogInformation(
            "Shop {ShopId} created from import candidate {CandidateId}",
            shop.Id,
            item.CandidateId);

        return shop.Id;
    }

    private async Task<Guid> ResolveCityId(Guid requestedCityId, CancellationToken ct)
    {
        if (await cityRepository.Exists(requestedCityId, ct))
            return requestedCityId;

        var cityName = CitiesConsts.Cities.GetValueOrDefault(requestedCityId) ?? "Минск";
        var city = await cityRepository.GetByName(cityName, ct)
                   ?? throw new DomainException($"City '{cityName}' not found. OSM import cannot publish without a matching Cities row.");

        logger.LogWarning(
            "Import CityId {RequestedCityId} is not in Cities; using {CityId} ({CityName}) instead",
            requestedCityId,
            city.Id,
            city.Name);

        return city.Id;
    }

    private static string? FirstPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var first = phone.Split(';', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            return null;

        return first.Length <= BusinessConstants.MaxShopContactPhoneNumberLength
            ? first
            : first[..BusinessConstants.MaxShopContactPhoneNumberLength];
    }
}
