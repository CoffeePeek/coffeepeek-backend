using System.Net;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;

public static class UpdateCityHandler
{
    public static async Task<Response<CityDto>> Handle(
        UpdateCityCommand command,
        ICityRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var city = await repository.GetByIdAsync(command.Id, ct);
        if (city is null)
            return Response<CityDto>.Error(HttpStatusCode.NotFound, "City not found.");

        city.Update(command.Name);
        return Response<CityDto>.Success(mapper.Map<CityDto>(city));
    }
}
