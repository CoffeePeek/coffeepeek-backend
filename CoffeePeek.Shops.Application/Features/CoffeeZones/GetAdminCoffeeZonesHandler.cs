using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class GetAdminCoffeeZonesHandler
{
    public static async Task<Response<AdminCoffeeZoneDto[]>> Handle(
        GetAdminCoffeeZonesQuery query, ICoffeeZoneQueries queries, CancellationToken ct) =>
        Response<AdminCoffeeZoneDto[]>.Success(await queries.GetAllAsync(query.CityId, ct));
}
