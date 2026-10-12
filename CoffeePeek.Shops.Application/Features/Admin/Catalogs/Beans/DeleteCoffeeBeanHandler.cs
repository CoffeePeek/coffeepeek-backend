using System.Net;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;

public static class DeleteCoffeeBeanHandler
{
    public static async Task<Response> Handle(
        DeleteCoffeeBeanCommand command,
        ICoffeeBeanRepository repository,
        CancellationToken ct)
    {
        var bean = await repository.GetByIdAsync(command.Id, ct);
        if (bean is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Coffee bean not found.");

        repository.Remove(bean);
        return Response.Success(message: "Coffee bean deleted.");
    }
}
