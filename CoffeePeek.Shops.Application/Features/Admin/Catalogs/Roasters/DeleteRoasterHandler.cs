using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;

public static class DeleteRoasterHandler
{
    public static async Task<Response> Handle(
        DeleteRoasterCommand command,
        IRoasterRepository repository,
        CancellationToken ct)
    {
        var roaster = await repository.GetByIdAsync(command.Id, ct);
        if (roaster is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Roaster not found.");

        repository.Remove(roaster);
        return Response.Success(message: "Roaster deleted.");
    }
}
