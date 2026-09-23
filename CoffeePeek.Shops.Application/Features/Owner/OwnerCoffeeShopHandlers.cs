using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.Owner;

public record GetOwnerCoffeeShopsQuery(Guid OwnerUserId);

public record GetOwnerCoffeeShopsResponse(IReadOnlyList<AdminPublishedShopDto> Items);

public static class GetOwnerCoffeeShopsHandler
{
    public static async Task<Response<GetOwnerCoffeeShopsResponse>> Handle(
        GetOwnerCoffeeShopsQuery query,
        IAdminCoffeeShopQueryRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var items = await repository.GetByOwnerUserIdAsync(query.OwnerUserId, ct);
        var media = mediaOptions.Value;
        var dtos = items.Select(s => AdminPublishedShopMapper.Map(s, media)).ToList();
        return Response<GetOwnerCoffeeShopsResponse>.Success(new GetOwnerCoffeeShopsResponse(dtos));
    }
}

public record GetOwnerCoffeeShopByIdQuery(Guid ShopId, Guid OwnerUserId);

public static class GetOwnerCoffeeShopByIdHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        GetOwnerCoffeeShopByIdQuery query,
        ICoffeeShopRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdWithCatalogsForOwnerAsync(query.ShopId, query.OwnerUserId, ct);
        return shop is null
            ? Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.")
            : Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}

public record UpdateOwnerCoffeeShopCommand(
    Guid ShopId,
    Guid OwnerUserId,
    string Name,
    string? Description,
    string? PhoneNumber,
    string? Email,
    string? SiteLink,
    string? InstagramLink,
    ShopLocationPatch? Location,
    IReadOnlyList<ScheduleDto>? Schedules,
    ShopCatalogsPatch? Catalogs);

public static class UpdateOwnerCoffeeShopHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        UpdateOwnerCoffeeShopCommand command,
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
        var shop = await repository.GetByIdWithCatalogsForOwnerAsync(command.ShopId, command.OwnerUserId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        shop.UpdateDetails(command.Name, command.Description, shop.PriceRange);

        var applied = await ShopProfileApplier.ApplyAsync(
            shop,
            new ShopProfilePatch(
                command.Location,
                new ShopContactsPatch(command.PhoneNumber, command.Email, command.SiteLink, command.InstagramLink),
                command.Schedules,
                command.Catalogs),
            cities, equipment, beans, roasters, brewMethods, ct);
        if (applied.IsFailed)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                applied.Errors[0].Message);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
