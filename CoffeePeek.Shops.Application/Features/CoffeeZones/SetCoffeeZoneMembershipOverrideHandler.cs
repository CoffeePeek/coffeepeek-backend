using System.Net;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class SetCoffeeZoneMembershipOverrideHandler
{
    public static async Task<Response<CoffeeZoneMembershipPreviewDto>> Handle(
        SetCoffeeZoneMembershipOverrideCommand command,
        IQueryCoffeeShopRepository shopRepository,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var zone = await repository.GetByIdAsync(command.ZoneId, ct);
        if (zone is null)
            return GetAdminCoffeeZoneHandler.NotFound<CoffeeZoneMembershipPreviewDto>();
        var shopCityId = await shopRepository.GetCityIdAsync(command.ShopId, ct);
        if (!shopCityId.HasValue)
            return Response<CoffeeZoneMembershipPreviewDto>.Error(HttpStatusCode.NotFound, "Coffee shop not found.");
        if (shopCityId.Value != zone.CityId)
            return Response<CoffeeZoneMembershipPreviewDto>.Error(
                HttpStatusCode.BadRequest, "Coffee shop and coffee zone must belong to the same city.");

        if (command.Kind == CoffeeZoneMembershipOverrideKind.Primary)
        {
            var previousPrimary = await repository.GetPrimaryOverrideAsync(command.ShopId, ct);
            if (previousPrimary is not null && previousPrimary.ZoneId != command.ZoneId)
                repository.RemoveOverride(previousPrimary);
        }

        var current = await repository.GetOverrideAsync(command.ZoneId, command.ShopId, ct);
        if (current is null)
            repository.AddOverride(new CoffeeZoneMembershipOverride(command.ZoneId, command.ShopId, command.Kind));
        else
            current.SetKind(command.Kind);

        // Flush for the SQL readback; Wolverine owns commit and rolls back a failed readback.
        await unitOfWork.SaveChangesAsync(ct);
        return Response<CoffeeZoneMembershipPreviewDto>.Success(
            (await queries.PreviewMembershipAsync(command.ZoneId, ct))!);
    }
}
