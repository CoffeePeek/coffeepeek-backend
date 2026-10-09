using System.Net;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;

public static class CreateCityHandler
{
    public static async Task<Response<CityDto>> Handle(
        CreateCityCommand command,
        IQueryCityRepository queryRepository,
        ICityRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var existing = await queryRepository.GetByName(command.Name, ct);
        if (existing is not null)
            return Response<CityDto>.Error(HttpStatusCode.Conflict, "A city with this name already exists.");

        var city = new City(command.Name);
        repository.Add(city);
        return Response<CityDto>.Success(mapper.Map<CityDto>(city));
    }
}
