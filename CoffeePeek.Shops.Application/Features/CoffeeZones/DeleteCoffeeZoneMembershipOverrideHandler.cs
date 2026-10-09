using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class DeleteCoffeeZoneMembershipOverrideHandler
{
    public static async Task<Response<CoffeeZoneMembershipPreviewDto>> Handle(
        DeleteCoffeeZoneMembershipOverrideCommand command,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (await repository.GetByIdAsync(command.ZoneId, ct) is null)
            return GetAdminCoffeeZoneHandler.NotFound<CoffeeZoneMembershipPreviewDto>();

        var current = await repository.GetOverrideAsync(command.ZoneId, command.ShopId, ct);
        if (current is not null)
        {
            repository.RemoveOverride(current);
            // Flush for the SQL readback; Wolverine owns commit and rolls back a failed readback.
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Response<CoffeeZoneMembershipPreviewDto>.Success(
            (await queries.PreviewMembershipAsync(command.ZoneId, ct))!);
    }
}
