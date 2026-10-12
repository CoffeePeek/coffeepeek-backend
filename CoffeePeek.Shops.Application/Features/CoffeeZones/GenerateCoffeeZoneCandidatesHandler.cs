using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public static class GenerateCoffeeZoneCandidatesHandler
{
    public static async Task<Response<CoffeeZoneCandidateDto[]>> Handle(
        GenerateCoffeeZoneCandidatesQuery query, ICoffeeZoneQueries queries, CancellationToken ct) =>
        Response<CoffeeZoneCandidateDto[]>.Success(
            await queries.GenerateCandidatesAsync(query.CityId, query.RadiusMeters, query.MinShops, ct));
}
