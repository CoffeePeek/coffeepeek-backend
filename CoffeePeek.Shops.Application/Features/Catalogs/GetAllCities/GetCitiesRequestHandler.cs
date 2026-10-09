using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetAllCities;

public class GetCitiesRequestHandler
{
    public async Task<Response<GetCitiesResponse>> Handle(
        GetCitiesCommand command,
        IMapper mapper,
        IQueryCityRepository queryCityRepository,
        CancellationToken cancellationToken)
    {
        var cities = await queryCityRepository.GetAll(cancellationToken);
        var result = mapper.Map<CityDto[]>(cities);

        var response = new GetCitiesResponse(result);

        return Response<GetCitiesResponse>.Success(response);
    }
}
