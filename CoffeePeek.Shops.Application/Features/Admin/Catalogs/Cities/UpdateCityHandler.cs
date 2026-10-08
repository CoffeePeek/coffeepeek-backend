using System.Net;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
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
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var city = await repository.GetByIdAsync(command.Id, ct);
        if (city is null)
            return Response<CityDto>.Error(HttpStatusCode.NotFound, "City not found.");

        city.Update(command.Name);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateCityHandler.InvalidateCityCachesAsync(cacheService, ct);

        return Response<CityDto>.Success(mapper.Map<CityDto>(city));
    }
}
