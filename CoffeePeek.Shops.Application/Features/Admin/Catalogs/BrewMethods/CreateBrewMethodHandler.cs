using System.Net;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;

public static class CreateBrewMethodHandler
{
    public static async Task<Response<BrewMethodDto>> Handle(
        CreateBrewMethodCommand command,
        IBrewMethodRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var existing = await repository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            return Response<BrewMethodDto>.Error(HttpStatusCode.Conflict, "A brew method with this name already exists.");

        var brewMethod = new BrewMethod(command.Name, (BrewMethodCategory)(int)command.Category);
        repository.Add(brewMethod);

        return Response<BrewMethodDto>.Success(mapper.Map<BrewMethodDto>(brewMethod));
    }

}
