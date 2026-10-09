using System.Net;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class SetCoffeeZoneStatusHandler
{
    public static async Task<Response<AdminCoffeeZoneDto>> Handle(
        SetCoffeeZoneStatusCommand command,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var zone = await repository.GetByIdAsync(command.Id, ct);
        if (zone is null)
            return GetAdminCoffeeZoneHandler.NotFound<AdminCoffeeZoneDto>();

        switch (command.Status)
        {
            case CoffeeZoneStatus.Draft: zone.MoveToDraft(); break;
            case CoffeeZoneStatus.Published: zone.Publish(); break;
            case CoffeeZoneStatus.Archived: zone.Archive(); break;
            default: return Response<AdminCoffeeZoneDto>.Error(HttpStatusCode.BadRequest, "Invalid coffee zone status.");
        }

        // Flush for the SQL readback; Wolverine owns commit and rolls back a failed readback.
        await unitOfWork.SaveChangesAsync(ct);
        return Response<AdminCoffeeZoneDto>.Success((await queries.GetByIdAsync(zone.Id, ct))!);
    }
}
