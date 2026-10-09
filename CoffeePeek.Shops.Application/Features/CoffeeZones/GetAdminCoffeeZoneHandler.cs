using System.Net;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class GetAdminCoffeeZoneHandler
{
    public static async Task<Response<AdminCoffeeZoneDto>> Handle(
        GetAdminCoffeeZoneQuery query, ICoffeeZoneQueries queries, CancellationToken ct)
    {
        var zone = await queries.GetByIdAsync(query.Id, ct);
        return zone is null ? NotFound<AdminCoffeeZoneDto>() : Response<AdminCoffeeZoneDto>.Success(zone);
    }

    internal static Response<T> NotFound<T>() =>
        Response<T>.Error(HttpStatusCode.NotFound, "Coffee zone not found.");
}
