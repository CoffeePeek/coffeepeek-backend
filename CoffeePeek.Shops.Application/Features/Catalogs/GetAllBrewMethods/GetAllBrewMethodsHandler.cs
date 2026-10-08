using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetAllBrewMethods;

public class GetAllBrewMethodsHandler
{
    public async Task<Response<GetAllBrewMethodsResponse>> Handle(
        GetAllBrewMethodsCommand command, 
        IQueryBrewMethodRepository repository,
        IMapper mapper,
        CancellationToken cancellationToken)
    {
        var methods = await repository.GetAll(cancellationToken);
        var result = mapper.Map<BrewMethodDto[]>(methods);

        var response = new GetAllBrewMethodsResponse(result);
        
        return Response<GetAllBrewMethodsResponse>.Success(response);
    }
}
