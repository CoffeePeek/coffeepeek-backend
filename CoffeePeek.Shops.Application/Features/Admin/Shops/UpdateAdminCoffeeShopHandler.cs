using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Wolverine;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

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
        IMessageBus bus,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        IAddressGeocoder geocoder,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdWithCatalogsAsync(command.ShopId, ct);
        if (shop is null)
            throw new NotFoundException("Shop not found.");

        if (command.ExpectedRevision is { } expectedRevision && expectedRevision != shop.ProfileRevision)
        {
            ShopsTelemetry.ProfileWriteConflict();
            throw new ConflictException("Shop profile changed; reload and retry.", statusCode: 409);
        }

        var priceRange = (DomainPriceRange)command.PriceRange;
        var details = DomainCoffeeShop.ValidateDetails(
            command.Name, command.Description, priceRange);
        if (details.IsFailed)
            throw new ValidationException(details.Errors[0].Message);

        if (command.Status is { } status)
        {
            var validStatus = DomainCoffeeShop.ValidateStatus(status);
            if (validStatus.IsFailed)
                throw new ValidationException(validStatus.Errors[0].Message);
        }

        var applied = await ShopProfileApplier.ApplyAsync(
            shop,
            new ShopProfilePatch(command.Location, command.Contacts, command.Schedules, command.Catalogs),
            cities, equipment, beans, roasters, brewMethods, geocoder, ct);
        if (applied.IsFailed)
            throw new ValidationException(applied.Errors[0].Message);

        shop.UpdateDetails(command.Name, command.Description, priceRange);
        if (command.Status.HasValue)
            shop.SetStatus(command.Status.Value);
        shop.AdvanceProfileRevision();

        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
