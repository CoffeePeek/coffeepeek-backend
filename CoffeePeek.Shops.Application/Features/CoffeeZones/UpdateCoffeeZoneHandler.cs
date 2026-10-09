using System.Net;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class UpdateCoffeeZoneHandler
{
    public static async Task<Response<AdminCoffeeZoneDto>> Handle(
        UpdateCoffeeZoneCommand command,
        IQueryCityRepository cityRepository,
        ICoffeeZoneRepository repository,
        ICoffeeZoneQueries queries,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var zone = await repository.GetByIdAsync(command.Id, ct);
        if (zone is null)
            return GetAdminCoffeeZoneHandler.NotFound<AdminCoffeeZoneDto>();
        if (!await cityRepository.Exists(command.CityId, ct))
            return Response<AdminCoffeeZoneDto>.Error(HttpStatusCode.BadRequest, "City not found.");

        zone.Update(command.CityId, command.Name, command.Description, command.Polygon, command.Color);
        // Flush for the SQL readback; Wolverine owns commit and rolls back a failed readback.
        await unitOfWork.SaveChangesAsync(ct);
        return Response<AdminCoffeeZoneDto>.Success((await queries.GetByIdAsync(zone.Id, ct))!);
    }
}
