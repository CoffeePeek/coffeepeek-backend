using System.Net;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Microsoft.Extensions.Options;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

/// <summary>Gallery photo metadata returned on admin/owner published shop responses.</summary>
public record AdminShopPhotoDto(
    Guid Id,
    string FileName,
    string ContentType,
    string StorageKey,
    string FullUrl,
    long SizeBytes,
    int SortIndex);

/// <summary>Address and coordinates of a published shop, as returned to admin and owner APIs.</summary>
public record AdminShopLocationDto(
    Guid CityId,
    string Address,
    decimal? Latitude,
    decimal? Longitude);

/// <summary>Public contact details of a published shop, as returned to admin and owner APIs.</summary>
public record AdminShopContactsDto(
    string? PhoneNumber,
    string? Email,
    string? SiteLink,
    string? InstagramLink);

/// <summary>Published coffee shop summary for admin and owner portals.</summary>
public record AdminPublishedShopDto(
    Guid Id,
    string Name,
    Guid CityId,
    DomainCoffeeShopStatus Status,
    CoffeeShopType? Type,
    Guid? CreatorId,
    Guid? OwnerUserId,
    Guid? ModerationId,
    DateTime CreatedAtUtc,
    bool IsHidden,
    DateTime? ImportedFromFileAt,
    IReadOnlyList<AdminShopPhotoDto> Photos,
    string? Description = null,
    ContractPriceRange? PriceRange = null,
    AdminShopLocationDto? Location = null,
    AdminShopContactsDto? Contacts = null,
    IReadOnlyList<ScheduleDto>? Schedules = null,
    IReadOnlyList<Guid>? EquipmentIds = null,
    IReadOnlyList<Guid>? BeanIds = null,
    IReadOnlyList<Guid>? RoasterIds = null,
    IReadOnlyList<Guid>? BrewMethodIds = null);

public record GetAdminCoffeeShopsResponse(
    IReadOnlyList<AdminPublishedShopDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);

public record GetAdminCoffeeShopsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    DomainCoffeeShopStatus? Status = null,
    bool? ImportedFromFile = null);

public static class AdminPublishedShopMapper
{
    public static AdminPublishedShopDto Map(DomainCoffeeShop shop, MediaPublicUrlOptions mediaOptions) => new(
        shop.Id,
        shop.Name,
        shop.Location?.CityId ?? Guid.Empty,
        shop.Status,
        shop.Type is null ? null : (CoffeeShopType)(int)shop.Type.Value,
        shop.CreatorId,
        shop.OwnerUserId,
        shop.ModerationId,
        shop.CreatedAtUtc,
        shop.Status != DomainCoffeeShopStatus.Active,
        shop.ImportedFromFileAt,
        MapPhotos(shop.ShopPhotos, mediaOptions),
        shop.Description,
        (ContractPriceRange)shop.PriceRange,
        shop.Location is null
            ? null
            : new AdminShopLocationDto(
                shop.Location.CityId,
                shop.Location.Address,
                shop.Location.Latitude,
                shop.Location.Longitude),
        shop.Contact is null
            ? null
            : new AdminShopContactsDto(
                shop.Contact.PhoneNumber,
                shop.Contact.Email,
                shop.Contact.SiteLink,
                shop.Contact.InstagramLink),
        shop.Schedules
            .Select(s => new ScheduleDto(
                s.DayOfWeek,
                s.IsClosed,
                s.Intervals?
                    .Select(i => new ShopScheduleIntervalDto
                    {
                        OpenTime = i.OpenTime,
                        CloseTime = i.CloseTime
                    })
                    .ToList() ?? []))
            .ToList(),
        shop.Equipments.Select(e => e.Id).ToList(),
        shop.CoffeeBeans.Select(b => b.Id).ToList(),
        shop.Roasters.Select(r => r.Id).ToList(),
        shop.BrewMethods.Select(m => m.Id).ToList());

    public static IReadOnlyList<AdminShopPhotoDto> MapPhotos(
        IReadOnlyCollection<ShopPhoto> photos,
        MediaPublicUrlOptions mediaOptions) =>
        photos
            .OrderBy(p => p.SortIndex)
            .ThenBy(p => p.CreatedAtUtc)
            .Select(p => new AdminShopPhotoDto(
                p.Id,
                p.FileName,
                p.ContentType,
                p.StorageKey,
                MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint,
                    mediaOptions.ShopBucketName,
                    p.StorageKey) ?? string.Empty,
                p.SizeBytes,
                p.SortIndex))
            .ToList();
}

public static class GetAdminCoffeeShopsHandler
{
    public static async Task<Response<GetAdminCoffeeShopsResponse>> Handle(
        GetAdminCoffeeShopsQuery query,
        IAdminCoffeeShopQueryRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, totalCount) = await repository.GetPagedAsync(
            page, pageSize, query.Search, query.Status, ct, query.ImportedFromFile);

        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var media = mediaOptions.Value;
        var dtos = items.Select(s => AdminPublishedShopMapper.Map(s, media)).ToList();

        return Response<GetAdminCoffeeShopsResponse>.Success(new GetAdminCoffeeShopsResponse(
            dtos, totalCount, totalPages, page, pageSize));
    }
}

public record GetAdminCoffeeShopByIdQuery(Guid ShopId);

public static class GetAdminCoffeeShopByIdHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        GetAdminCoffeeShopByIdQuery query,
        ICoffeeShopRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdWithCatalogsAsync(query.ShopId, ct);
        return shop is null
            ? Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.")
            : Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}

public record UpdateAdminCoffeeShopCommand(
    Guid ShopId,
    string Name,
    string? Description,
    ContractPriceRange PriceRange,
    DomainCoffeeShopStatus? Status,
    ShopLocationPatch? Location,
    ShopContactsPatch? Contacts,
    IReadOnlyList<ScheduleDto>? Schedules,
    ShopCatalogsPatch? Catalogs);

public static class UpdateAdminCoffeeShopHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        UpdateAdminCoffeeShopCommand command,
        ICoffeeShopRepository repository,
        IQueryCityRepository cities,
        IQueryEquipmentRepository equipment,
        IQueryCoffeeBeanRepository beans,
        IQueryRoasterRepository roasters,
        IQueryBrewMethodRepository brewMethods,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdWithCatalogsAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        shop.UpdateDetails(command.Name, command.Description, (DomainPriceRange)command.PriceRange);
        if (command.Status.HasValue)
            shop.SetStatus(command.Status.Value);

        var applied = await ShopProfileApplier.ApplyAsync(
            shop,
            new ShopProfilePatch(command.Location, command.Contacts, command.Schedules, command.Catalogs),
            cities, equipment, beans, roasters, brewMethods, ct);
        if (applied.IsFailed)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                applied.Errors[0].Message);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.ListPattern(), ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}

public record SetAdminCoffeeShopVisibilityCommand(Guid ShopId, bool Hidden);

public static class SetAdminCoffeeShopVisibilityHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        SetAdminCoffeeShopVisibilityCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        shop.SetHidden(command.Hidden);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}

public record AssignCoffeeShopOwnerCommand(Guid ShopId, Guid? OwnerUserId);

public static class AssignCoffeeShopOwnerHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        AssignCoffeeShopOwnerCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        shop.AssignOwner(command.OwnerUserId);

        await unitOfWork.SaveChangesAsync(ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}

public record SetAdminCoffeeShopFocusCommand(Guid ShopId, CoffeeShopType Type, Guid AdminUserId);

public static class SetAdminCoffeeShopFocusHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        SetAdminCoffeeShopFocusCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(HttpStatusCode.NotFound, "Shop not found.");

        shop.SetCoffeeFocus((CoffeeFocusType)(int)command.Type);

        var tagIds = shop.ShopTags.Select(t => t.TagId).ToList();
        if (command.Type == CoffeeShopType.Specialty)
        {
            if (!tagIds.Contains(ShopTagIds.Specialty))
                tagIds.Add(ShopTagIds.Specialty);
        }
        else
        {
            tagIds.Remove(ShopTagIds.Specialty);
        }

        shop.SetTags(tagIds, command.AdminUserId);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
