using System.Net;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;

public static class UpdateBrewMethodHandler
{
    public static async Task<Response<BrewMethodDto>> Handle(
        UpdateBrewMethodCommand command,
        IBrewMethodRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var brewMethod = await repository.GetByIdAsync(command.Id, ct);
        if (brewMethod is null)
            return Response<BrewMethodDto>.Error(HttpStatusCode.NotFound, "Brew method not found.");

        brewMethod.Update(command.Name, (BrewMethodCategory)(int)command.Category);

        return Response<BrewMethodDto>.Success(mapper.Map<BrewMethodDto>(brewMethod));
    }
}
