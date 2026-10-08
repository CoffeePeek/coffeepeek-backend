using System.Net;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;

public static class DeleteCityHandler
{
    public static async Task<Response> Handle(
        DeleteCityCommand command,
        ICityRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var result = await repository.DeleteIfUnusedAsync(command.Id, ct);
        if (result == CityDeleteResult.NotFound)
            return Response.Error((int)HttpStatusCode.NotFound, "City not found.");
        if (result == CityDeleteResult.Referenced)
            return Response.Error((int)HttpStatusCode.Conflict, "City is referenced by shops, roasters or zones.");

        await CreateCityHandler.InvalidateCityCachesAsync(cacheService, ct);
        return Response.Success(message: "City deleted.");
    }
}
