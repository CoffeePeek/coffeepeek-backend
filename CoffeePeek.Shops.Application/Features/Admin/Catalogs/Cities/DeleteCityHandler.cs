using System.Net;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;

public static class DeleteCityHandler
{
    public static async Task<Response> Handle(
        DeleteCityCommand command,
        ICityRepository repository,
        CancellationToken ct)
    {
        var result = await repository.DeleteIfUnusedAsync(command.Id, ct);
        if (result == CityDeleteResult.NotFound)
            return Response.Error((int)HttpStatusCode.NotFound, "City not found.");
        if (result == CityDeleteResult.Referenced)
            return Response.Error((int)HttpStatusCode.Conflict, "City is referenced by shops, roasters or zones.");

        return Response.Success(message: "City deleted.");
    }
}
