using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Wolverine;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.Owner;

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
        IMessageBus bus,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        IAddressGeocoder geocoder,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdWithCatalogsForOwnerAsync(command.ShopId, command.OwnerUserId, ct);
        if (shop is null)
            throw new NotFoundException("Shop not found.");

        if (command.ExpectedRevision is { } expectedRevision && expectedRevision != shop.ProfileRevision)
        {
            ShopsTelemetry.ProfileWriteConflict();
            throw new ConflictException("Shop profile changed; reload and retry.", statusCode: 409);
        }

        var details = DomainCoffeeShop.ValidateDetails(command.Name, command.Description, shop.PriceRange);
        if (details.IsFailed)
            throw new ValidationException(details.Errors[0].Message);

        var applied = await ShopProfileApplier.ApplyAsync(
            shop,
            new ShopProfilePatch(
                command.Location,
                new ShopContactsPatch(command.PhoneNumber, command.Email, command.SiteLink, command.InstagramLink),
                command.Schedules,
                command.Catalogs),
            cities, equipment, beans, roasters, brewMethods, geocoder, ct);
        if (applied.IsFailed)
            throw new ValidationException(applied.Errors[0].Message);

        shop.UpdateDetails(command.Name, command.Description, shop.PriceRange);
        shop.AdvanceProfileRevision();

        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
