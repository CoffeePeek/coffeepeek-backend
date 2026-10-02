using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Shared.Kernel.Exceptions;
using Mapster;

namespace CoffeePeek.Moderation.Application.Services;

public sealed class PublicModerationService(IPublicAddressLookup addresses)
{
    public async Task<Guid> ResolveIdAsync(string kind, string slug, CancellationToken ct) =>
        (await addresses.ResolveAsync(kind, slug, ct))?.EntityId ?? throw new NotFoundException("Entity not found.");

    public async Task<ShopChangePayloadDto> PayloadAsync(PublicShopChangePayloadDto x, CancellationToken ct)
    {
        var tagsTask = FilterAsync("shop-tags", x.Tags, ct);
        var roastersTask = FilterAsync("roasters", x.Roasters, ct);
        var equipmentsTask = FilterAsync("equipments", x.Equipments, ct);
        var methodsTask = FilterAsync("brew-methods", x.BrewMethods, ct);
        await Task.WhenAll(tagsTask, roastersTask, equipmentsTask, methodsTask);
        return new(x.Description, x.Contacts, x.Photos, await tagsTask, await roastersTask,
            await equipmentsTask, x.Menu, await methodsTask);
    }

    public Task<Guid[]> ResolveIdsAsync(string kind, string[] slugs, CancellationToken ct) => addresses.ResolveIdsAsync(kind, slugs, ct);
    private async Task<Guid[]?> FilterAsync(string kind, string[]? slugs, CancellationToken ct) =>
        slugs is null ? null : await addresses.ResolveIdsAsync(kind, slugs, ct);

    public async Task<PublicShopChangeRequestDto[]> ChangesAsync(IReadOnlyList<ShopChangeRequestDto> items, CancellationToken ct)
    {
        var shopsTask = ReadAsync("shops", items.Select(x => x.ShopId), ct);
        var roastersTask = ReadAsync("roasters", items.SelectMany(x => x.Payload.RoasterIds ?? []), ct);
        var tagsTask = ReadAsync("shop-tags", items.SelectMany(x => x.Payload.TagIds ?? []), ct);
        var equipmentsTask = ReadAsync("equipments", items.SelectMany(x => x.Payload.EquipmentIds ?? []), ct);
        var methodsTask = ReadAsync("brew-methods", items.SelectMany(x => x.Payload.BrewMethodIds ?? []), ct);
        await Task.WhenAll(shopsTask, roastersTask, tagsTask, equipmentsTask, methodsTask);
        var shops = await shopsTask;
        var roasters = await roastersTask;
        var tags = await tagsTask;
        var equipments = await equipmentsTask;
        var methods = await methodsTask;
        return items.Select(x => new PublicShopChangeRequestDto(x.Id, shops.GetValueOrDefault(x.ShopId), x.Section,
            new(x.Payload.Description, x.Payload.Contacts, x.Payload.Photos, Slugs(x.Payload.TagIds, tags),
                Slugs(x.Payload.RoasterIds, roasters), Slugs(x.Payload.EquipmentIds, equipments),
                x.Payload.Menu, Slugs(x.Payload.BrewMethodIds, methods)),
            x.Status, x.ReviewedAtUtc, x.RejectionReason, x.CreatedAtUtc, x.UpdatedAtUtc)).ToArray();
    }

    public async Task<PublicModerationReviewDto[]> ReviewsAsync(ModerationReviewDto[] items, CancellationToken ct)
    {
        var shops = await ReadAsync("shops", items.Select(x => x.ShopId), ct);
        return items.Select(x => x.Adapt<PublicModerationReviewDto>() with { Shop = shops.GetValueOrDefault(x.ShopId) }).ToArray();
    }

    public async Task<PublicModerationShopDto[]> ShopsAsync(ModerationShopDto[] items, CancellationToken ct)
    {
        var cities = await ReadAsync("cities", items.Where(x => x.CityId.HasValue).Select(x => x.CityId!.Value), ct);
        var published = await ReadAsync("shops", items.Where(x => x.PublishedShopId.HasValue).Select(x => x.PublishedShopId!.Value), ct);
        var roasters = await ReadAsync("roasters", items.SelectMany(x => x.RoasterIds ?? []), ct);
        var beans = await ReadAsync("beans", items.SelectMany(x => x.CoffeeBeanIds ?? []), ct);
        var equipments = await ReadAsync("equipments", items.SelectMany(x => x.EquipmentIds ?? []), ct);
        var methods = await ReadAsync("brew-methods", items.SelectMany(x => x.BrewMethodIds ?? []), ct);
        return items.Select(x => x.Adapt<PublicModerationShopDto>() with
        {
            City = x.CityId is { } city ? cities.GetValueOrDefault(city) : null,
            PublishedShop = x.PublishedShopId is { } shop ? published.GetValueOrDefault(shop) : null,
            Roasters = Slugs(x.RoasterIds, roasters) ?? [], Beans = Slugs(x.CoffeeBeanIds, beans) ?? [],
            Equipments = Slugs(x.EquipmentIds, equipments) ?? [], BrewMethods = Slugs(x.BrewMethodIds, methods) ?? []
        }).ToArray();
    }

    public async Task<PublicModerationRoasterDto[]> RoastersAsync(ModerationRoasterDto[] items, CancellationToken ct)
    {
        var cities = await ReadAsync("cities", items.Where(x => x.CityId.HasValue).Select(x => x.CityId!.Value), ct);
        return items.Select(x => new PublicModerationRoasterDto(x.Id, x.Name, x.About, x.ModerationStatus, x.RejectedReason,
            x.CityId is { } id ? cities.GetValueOrDefault(id) : null, x.Location, x.Contact, x.Photos.Adapt<CoffeePeek.Contract.Dtos.ShortPhotoMetadataDto[]>())).ToArray();
    }

    private static string[]? Slugs(IEnumerable<Guid>? ids, Dictionary<Guid, PublicAddressDto> addresses) =>
        ids?.Where(addresses.ContainsKey).Select(id => addresses[id].Slug).ToArray();

    private async Task<Dictionary<Guid, PublicAddressDto>> ReadAsync(string kind, IEnumerable<Guid> ids, CancellationToken ct) =>
        (await addresses.GetByIdsAsync(kind, ids.Distinct().ToArray(), ct)).ToDictionary(x => x.EntityId, PublicAddressDto.From);
}
