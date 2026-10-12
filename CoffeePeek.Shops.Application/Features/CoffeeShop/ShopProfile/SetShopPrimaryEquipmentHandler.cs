using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

public static class SetShopPrimaryEquipmentHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        SetShopPrimaryEquipmentCommand command,
        ICoffeeShopRepository repository,
        IMessageBus bus,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = command.OwnerUserId is { } ownerId
            ? await repository.GetByIdWithCatalogsForOwnerAsync(command.ShopId, ownerId, ct)
            : await repository.GetByIdWithCatalogsAsync(command.ShopId, ct);
        if (shop is null)
            throw new NotFoundException("Shop not found.");

        var selected = command.PrimaryEquipmentIds ?? throw new ValidationException("Primary equipment IDs are required.");
        var result = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop
            .ValidateEquipmentSelection(shop.Equipments, selected);
        if (result.IsFailed)
            throw new ValidationException(result.Errors[0].Message);

        // PostgreSQL checks the partial unique index per UPDATE. Clear only the old
        // choices being replaced before EF writes the new choices in this transaction.
        var superseded = shop.ShopEquipments
            .Where(a => a.IsPrimary && !selected.Contains(a.EquipmentId))
            .Select(a => a.EquipmentId).ToArray();
        await repository.ClearSupersededPrimaryEquipmentAsync(shop.Id, superseded, ct);
        result = shop.SetEquipment(shop.Equipments, selected);
        if (result.IsFailed)
            throw new ValidationException(result.Errors[0].Message);

        shop.AdvanceProfileRevision();

        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
