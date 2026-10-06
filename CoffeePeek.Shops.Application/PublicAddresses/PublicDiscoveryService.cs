using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllCities;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllRoasters;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBeans;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllEquipment;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBrewMethods;
using CoffeePeek.Shops.Application.Features.Catalogs.GetShopTags;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Mapster;
using Wolverine;

namespace CoffeePeek.Shops.Application.PublicAddresses;

// Composes public read models; aggregate identities and existing command handlers stay internal.
public sealed class PublicDiscoveryService(IPublicAddressService addresses, IProfileAddressLookup profiles,
    ICoffeeShopQueries queries, IQueryCheckInRepository checkIns, IMessageBus bus)
{
    private static readonly TypeAdapterConfig PublicMapping = CreateMapping();
    private static TypeAdapterConfig CreateMapping()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<ShortShopDto, PublicShopSummaryDto>()
            .Ignore(x => x.Roasters, x => x.Beans, x => x.Equipments, x => x.BrewMethods);
        config.NewConfig<CoffeeShopDetailsDto, PublicShopDetailsDto>()
            .Ignore(x => x.Roasters, x => x.Beans, x => x.Equipments, x => x.BrewMethods,
                x => x.Reviews, x => x.UserCheckIns, x => x.Tags);
        return config;
    }
    public async Task<Guid> ResolveIdAsync(PublicAddressKind kind, string slug, CancellationToken ct) =>
        (await addresses.ResolveAsync(kind, slug, ct))?.EntityId ?? throw new NotFoundException("Entity not found.");

    public async Task<PublicCoffeeShopsResponse> SearchAsync(SearchCoffeeShopsQuery query, string? city,
        string[]? roasters, string[]? equipments, string[]? beans, string[]? brewMethods, string[]? tags, CancellationToken ct)
    {
        // Resolve public filters once. Never silently ignore unknown filters and return all shops.
        var cityId = city is null ? (Guid?)null : await ResolveIdAsync(PublicAddressKind.City, city, ct);
        Guid[]? roasterIds = null;
        if (roasters is not null)
        {
            roasterIds = await addresses.ResolveIdsAsync(PublicAddressKind.Roaster, roasters, ct);
        }
        query = query with
        {
            CityId = cityId, Roasters = roasterIds,
            Equipments = await FilterAsync(PublicAddressKind.Equipment, equipments, ct),
            Beans = await FilterAsync(PublicAddressKind.CoffeeBean, beans, ct),
            BrewMethods = await FilterAsync(PublicAddressKind.BrewMethod, brewMethods, ct),
            Tags = await FilterAsync(PublicAddressKind.ShopTag, tags, ct)
        };
        // Current visibility and pagination come from the database, rather than a legacy cached GUID page.
        var (items, count) = await queries.Search(query, ct);
        if (query.UserId is { } userId)
        {
            var visited = await checkIns.GetVisitedShopIdsAsync(userId, ct);
            foreach (var item in items) item.IsVisited = visited.Contains(item.Id);
        }
        var shops = await ReadAsync(PublicAddressKind.CoffeeShop, items.Select(x => x.Id), ct);
        var cities = await ReadAsync(PublicAddressKind.City, items.Select(x => x.CityId), ct);
        var roasterAddresses = await ReadAsync(PublicAddressKind.Roaster, items.SelectMany(x => x.Roasters ?? []).Select(x => x.Id), ct);
        var beanAddresses = await ReadAsync(PublicAddressKind.CoffeeBean, items.SelectMany(x => x.Beans ?? []).Select(x => x.Id), ct);
        var equipmentAddresses = await ReadAsync(PublicAddressKind.Equipment, items.SelectMany(x => x.Equipments ?? []).Select(x => x.Id), ct);
        var methodAddresses = await ReadAsync(PublicAddressKind.BrewMethod, items.SelectMany(x => x.BrewMethods ?? []).Select(x => x.Id), ct);
        var result = items.Select(x => x.Adapt<PublicShopSummaryDto>(PublicMapping) with
        {
            Address = Required(shops, x.Id), City = Find(cities, x.CityId),
            Roasters = Roasters(x.Roasters, roasterAddresses),
            Beans = Beans(x.Beans, beanAddresses), Equipments = Equipments(x.Equipments, equipmentAddresses),
            BrewMethods = Methods(x.BrewMethods, methodAddresses)
        }).ToArray();
        return new(result, count, (int)Math.Ceiling(count / (double)query.PageSize), query.PageNumber, query.PageSize);
    }

    public async Task<PublicShopDetailsDto?> GetShopAsync(string slug, Guid? userId, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.CoffeeShop, slug, ct);
        if (address is null) return null;
        var response = await bus.InvokeAsync<Response<GetCoffeeShopResponse>>(new GetCoffeeShopQuery(address.EntityId, userId), ct);
        if (response is not { IsSuccess: true, Data: not null }) return null;
        var x = response.Data.ShopDto;
        var cities = await ReadAsync(PublicAddressKind.City, [x.CityId], ct);
        var roasters = await ReadAsync(PublicAddressKind.Roaster, (x.Roasters ?? []).Select(r => r.Id), ct);
        var beans = await ReadAsync(PublicAddressKind.CoffeeBean, (x.CoffeeBeans ?? []).Select(r => r.Id), ct);
        var equipments = await ReadAsync(PublicAddressKind.Equipment, (x.Equipments ?? []).Select(r => r.Id), ct);
        var methods = await ReadAsync(PublicAddressKind.BrewMethod, (x.BrewMethods ?? []).Select(r => r.Id), ct);
        var tags = await ReadAsync(PublicAddressKind.ShopTag, x.Tags.Select(r => r.Id), ct);
        var reviews = await ReviewsAsync(x.Reviews, ct);
        var visits = await CheckInsAsync(x.UserCheckIns, ct);
        // Cached details never decide current visibility, including a hide racing the data read.
        if (await addresses.ResolveAsync(PublicAddressKind.CoffeeShop, slug, ct) is not { } current) return null;
        return x.Adapt<PublicShopDetailsDto>(PublicMapping) with
        {
            Address = PublicAddressDto.From(current), City = Find(cities, x.CityId), Beans = Beans(x.CoffeeBeans, beans),
            Equipments = Equipments(x.Equipments, equipments), BrewMethods = Methods(x.BrewMethods, methods),
            Tags = Tags(x.Tags, tags),
            Roasters = Roasters(x.Roasters, roasters), Reviews = reviews, UserCheckIns = visits
        };
    }

    public async Task<PublicCityDto[]> CitiesAsync(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetCitiesResponse>>(new GetCitiesCommand(), ct);
        var cities = response.Data?.Cities ?? throw new ServiceUnavailableException("Cities unavailable.");
        var lookup = await ReadAsync(PublicAddressKind.City, cities.Select(x => x.Id), ct);
        return cities.Select(x => new PublicCityDto(Required(lookup, x.Id), x.Name)).ToArray();
    }

    public async Task<PublicRoasterSummaryDto[]> RoastersAsync(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetAllRoastersResponse>>(new GetAllRoastersCommand(), ct);
        var roasters = response.Data?.Roasters ?? throw new ServiceUnavailableException("Roasters unavailable.");
        var lookup = await ReadAsync(PublicAddressKind.Roaster, roasters.Select(x => x.Id), ct);
        return roasters.Select(x => new PublicRoasterSummaryDto(Required(lookup, x.Id), x.Name, x.PhotoUrl, x.CoverPhoto)
        { CoffeeShopsCount = x.CoffeeShopsCount, CoffeeProductsCount = x.CoffeeProductsCount }).ToArray();
    }

    public async Task<PublicRoasterDetailsDto?> GetRoasterAsync(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.Roaster, slug, ct);
        if (address is null) return null;
        var response = await bus.InvokeAsync<Response<RoasterDetailsDto>>(new GetRoasterByIdQuery(address.EntityId), ct);
        if (response is not { IsSuccess: true, Data: not null }) return null;
        var x = response.Data;
        var shops = await ReadAsync(PublicAddressKind.CoffeeShop, x.Shops.Select(s => s.Id), ct);
        return new(PublicAddressDto.From(address), x.Name, x.About, x.Location, x.Contact, x.Photos,
            x.Shops.Where(s => shops.ContainsKey(s.Id)).Select(s => new PublicLinkedShopDto(Find(shops, s.Id), s.Name, s.CoverPhoto)).ToArray())
        { CoffeeShopsCount = x.CoffeeShopsCount, CoffeeProductsCount = x.CoffeeProductsCount };
    }

    public async Task<PublicReviewDto[]> ReviewsAsync(ReviewDto[] items, CancellationToken ct)
    {
        var shops = await ReadAsync(PublicAddressKind.CoffeeShop, items.Select(x => x.CoffeeShopId), ct);
        var authors = (await profiles.GetByIdsAsync(items.Select(x => x.UserId).Distinct().ToArray(), ct))
            .ToDictionary(x => x.EntityId, PublicAddressDto.From);
        return items.Select(x => x.Adapt<PublicReviewDto>() with
        { Shop = Find(shops, x.CoffeeShopId), Author = Find(authors, x.UserId) }).ToArray();
    }

    public async Task<PublicCheckInDto[]> CheckInsAsync(CheckInDto[] items, CancellationToken ct)
    {
        var shops = await ReadAsync(PublicAddressKind.CoffeeShop, items.Select(x => x.ShopId), ct);
        return items.Select(x => x.Adapt<PublicCheckInDto>() with { Shop = Find(shops, x.ShopId) }).ToArray();
    }

    public async Task<PublicMapDto> MapAsync(MapShopDto[] shops, MapClusterDto[]? clusters, MapCoffeeZoneDto[]? zones,
        bool? truncated, CancellationToken ct)
    {
        var shopAddresses = await ReadAsync(PublicAddressKind.CoffeeShop, shops.Select(x => x.Id), ct);
        var zoneAddresses = await ReadAsync(PublicAddressKind.CoffeeZone,
            (zones ?? []).Select(x => x.Id).Concat(shops.Where(x => x.PrimaryZoneId.HasValue).Select(x => x.PrimaryZoneId!.Value)), ct);
        return new(shops.Select(x => new PublicMapShopDto(Required(shopAddresses, x.Id), x.Latitude, x.Longitude,
                x.Title, x.Type, x.PrimaryZoneId is { } id ? Find(zoneAddresses, id) : null)).ToArray(),
            clusters ?? [], (zones ?? []).Select(x => new PublicZoneDto(Required(zoneAddresses, x.Id), x.Name, x.Description,
                x.Latitude, x.Longitude, x.RadiusMeters, x.ShopCount, x.Polygon, x.Color)).ToArray(), truncated ?? false);
    }

    private async Task<Dictionary<Guid, PublicAddressDto>> ReadAsync(PublicAddressKind kind, IEnumerable<Guid> ids, CancellationToken ct) =>
        (await addresses.GetForReadAsync(kind, ids.Where(x => x != Guid.Empty).Distinct().ToArray(), ct))
        .ToDictionary(x => x.EntityId, PublicAddressDto.From);

    private Task<Guid[]?> FilterAsync(PublicAddressKind kind, string[]? slugs, CancellationToken ct) =>
        slugs is null ? Task.FromResult<Guid[]?>(null) : ResolveFilterAsync(kind, slugs, ct);
    private async Task<Guid[]?> ResolveFilterAsync(PublicAddressKind kind, string[] slugs, CancellationToken ct) =>
        await addresses.ResolveIdsAsync(kind, slugs, ct);

    public async Task<PublicBeanDto[]> BeansAsync(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetAllCoffeeBeansResponse>>(new GetAllBeansCommand(), ct);
        var items = response.Data?.Beans ?? throw new ServiceUnavailableException("Beans unavailable.");
        return Beans(items, await ReadAsync(PublicAddressKind.CoffeeBean, items.Select(x => x.Id), ct));
    }
    public async Task<PublicEquipmentDto[]> EquipmentsAsync(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetAllEquipmentResponse>>(new GetAllEquipmentCommand(), ct);
        var items = response.Data?.Equipments ?? throw new ServiceUnavailableException("Equipment unavailable.");
        return Equipments(items, await ReadAsync(PublicAddressKind.Equipment, items.Select(x => x.Id), ct));
    }
    public async Task<PublicBrewMethodDto[]> MethodsAsync(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetAllBrewMethodsResponse>>(new GetAllBrewMethodsCommand(), ct);
        var items = response.Data?.BrewMethods ?? throw new ServiceUnavailableException("Brew methods unavailable.");
        return Methods(items, await ReadAsync(PublicAddressKind.BrewMethod, items.Select(x => x.Id), ct));
    }
    public async Task<PublicTagDto[]> TagsAsync(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetShopTagsResponse>>(new GetShopTagsCommand(), ct);
        var items = response.Data?.Tags ?? throw new ServiceUnavailableException("Tags unavailable.");
        return Tags(items, await ReadAsync(PublicAddressKind.ShopTag, items.Select(x => x.Id), ct));
    }

    private static PublicBeanDto[] Beans(CoffeeBeansDto[]? items, Dictionary<Guid, PublicAddressDto> addresses) =>
        (items ?? []).Select(x => new PublicBeanDto(Required(addresses, x.Id).Slug, x.Name)).ToArray();
    private static PublicEquipmentDto[] Equipments(EquipmentDto[]? items, Dictionary<Guid, PublicAddressDto> addresses) =>
        (items ?? []).Select(x => new PublicEquipmentDto(Required(addresses, x.Id).Slug, x.Name, x.Brand, x.Model, x.Category)).ToArray();
    private static PublicBrewMethodDto[] Methods(BrewMethodDto[]? items, Dictionary<Guid, PublicAddressDto> addresses) =>
        (items ?? []).Select(x => new PublicBrewMethodDto(Required(addresses, x.Id).Slug, x.Name, x.Category)).ToArray();
    private static PublicTagDto[] Tags(ShopTagDto[] items, Dictionary<Guid, PublicAddressDto> addresses) =>
        items.Where(x => addresses.ContainsKey(x.Id)).Select(x => new PublicTagDto(addresses[x.Id].Slug, x.Name, x.Description, x.SortOrder)).ToArray();

    private static PublicRoasterSummaryDto[] Roasters(RoasterDto[]? items, Dictionary<Guid, PublicAddressDto> addresses) =>
        (items ?? []).Select(x => new PublicRoasterSummaryDto(Find(addresses, x.Id), x.Name, x.PhotoUrl, x.CoverPhoto)
        { CoffeeShopsCount = x.CoffeeShopsCount, CoffeeProductsCount = x.CoffeeProductsCount }).ToArray();

    private static PublicAddressDto? Find(Dictionary<Guid, PublicAddressDto> addresses, Guid id) => addresses.GetValueOrDefault(id);
    private static PublicAddressDto Required(Dictionary<Guid, PublicAddressDto> addresses, Guid id) =>
        Find(addresses, id) ?? throw new ServiceUnavailableException("Public addresses are not ready. Complete the backfill.");
}
