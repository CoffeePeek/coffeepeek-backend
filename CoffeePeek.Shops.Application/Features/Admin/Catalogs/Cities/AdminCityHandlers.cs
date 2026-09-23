using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;

public record CreateCityCommand(string Name);

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

public record UpdateCityCommand([property: JsonIgnore] Guid Id, string Name);

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

public record DeleteCityCommand([property: JsonIgnore] Guid Id);

public static class DeleteCityHandler
{
    public static async Task<Response> Handle(
        DeleteCityCommand command,
        ICityRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var city = await repository.GetByIdAsync(command.Id, ct);
        if (city is null)
            return Response.Error((int)HttpStatusCode.NotFound, "City not found.");

        repository.Remove(city);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateCityHandler.InvalidateCityCachesAsync(cacheService, ct);

        return Response.Success(message: "City deleted.");
    }
}
