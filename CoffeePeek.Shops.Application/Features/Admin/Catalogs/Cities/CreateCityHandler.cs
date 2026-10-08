using System.Net;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
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
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var existing = await queryRepository.GetByName(command.Name, ct);
        if (existing is not null)
            return Response<CityDto>.Error(HttpStatusCode.Conflict, "A city with this name already exists.");

        var city = new City(command.Name);
        repository.Add(city);
        await unitOfWork.SaveChangesAsync(ct);
        await InvalidateCityCachesAsync(cacheService, ct);

        return Response<CityDto>.Success(mapper.Map<CityDto>(city));
    }

    internal static async Task InvalidateCityCachesAsync(ICacheService cacheService, CancellationToken ct)
    {
        await cacheService.RemoveByPattern(CacheKey.City.ListPattern(), ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }
}
