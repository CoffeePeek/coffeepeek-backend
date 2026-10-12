using System.Net;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;

public static class DeleteBrewMethodHandler
{
    public static async Task<Response> Handle(
        DeleteBrewMethodCommand command,
        IBrewMethodRepository repository,
        CancellationToken ct)
    {
        var brewMethod = await repository.GetByIdAsync(command.Id, ct);
        if (brewMethod is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Brew method not found.");

        repository.Remove(brewMethod);

        return Response.Success(message: "Brew method deleted.");
    }
}
