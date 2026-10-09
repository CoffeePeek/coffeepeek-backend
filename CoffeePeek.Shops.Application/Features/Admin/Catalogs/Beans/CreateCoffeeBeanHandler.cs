using System.Net;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;

public static class CreateCoffeeBeanHandler
{
    public static async Task<Response<CoffeeBeansDto>> Handle(
        CreateCoffeeBeanCommand command,
        ICoffeeBeanRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var existing = await repository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            return Response<CoffeeBeansDto>.Error(HttpStatusCode.Conflict, "A coffee bean with this name already exists.");

        var bean = new CoffeeBean(command.Name);
        repository.Add(bean);
        return Response<CoffeeBeansDto>.Success(mapper.Map<CoffeeBeansDto>(bean));
    }
}
