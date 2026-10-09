using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetAllBeans;

public class GetAllBeansHandler
{
    public async Task<Response<GetAllCoffeeBeansResponse>> Handle(GetAllBeansCommand _, 
        IQueryCoffeeBeanRepository repository,
        IMapper mapper,
        CancellationToken cancellationToken)
    {
        var coffeeBeans = await repository.GetAll(cancellationToken);
        var result = mapper.Map<CoffeeBeansDto[]>(coffeeBeans);
        
        var response = new GetAllCoffeeBeansResponse(result);

        return Response<GetAllCoffeeBeansResponse>.Success(response);
    }
}
