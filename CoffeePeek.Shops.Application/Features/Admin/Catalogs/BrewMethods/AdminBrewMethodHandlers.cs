using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;

public record CreateBrewMethodCommand(string Name, BrewMethodCategoryEnum Category);

public static class CreateBrewMethodHandler
{
    public static async Task<Response<BrewMethodDto>> Handle(
        CreateBrewMethodCommand command,
        IBrewMethodRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var existing = await repository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            return Response<BrewMethodDto>.Error(HttpStatusCode.Conflict, "A brew method with this name already exists.");

        var brewMethod = new BrewMethod(command.Name, (BrewMethodCategory)(int)command.Category);
        repository.Add(brewMethod);
        await unitOfWork.SaveChangesAsync(ct);
        await InvalidateBrewMethodCachesAsync(cacheService, ct);

        return Response<BrewMethodDto>.Success(mapper.Map<BrewMethodDto>(brewMethod));
    }

    internal static async Task InvalidateBrewMethodCachesAsync(ICacheService cacheService, CancellationToken ct)
    {
        await cacheService.RemoveByPattern(CacheKey.BrewMethod.ListPattern(), ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }
}

public record UpdateBrewMethodCommand([property: JsonIgnore] Guid Id, string Name, BrewMethodCategoryEnum Category);

public static class UpdateBrewMethodHandler
{
    public static async Task<Response<BrewMethodDto>> Handle(
        UpdateBrewMethodCommand command,
        IBrewMethodRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var brewMethod = await repository.GetByIdAsync(command.Id, ct);
        if (brewMethod is null)
            return Response<BrewMethodDto>.Error(HttpStatusCode.NotFound, "Brew method not found.");

        brewMethod.Update(command.Name, (BrewMethodCategory)(int)command.Category);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateBrewMethodHandler.InvalidateBrewMethodCachesAsync(cacheService, ct);

        return Response<BrewMethodDto>.Success(mapper.Map<BrewMethodDto>(brewMethod));
    }
}

public record DeleteBrewMethodCommand([property: JsonIgnore] Guid Id);

public static class DeleteBrewMethodHandler
{
    public static async Task<Response> Handle(
        DeleteBrewMethodCommand command,
        IBrewMethodRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var brewMethod = await repository.GetByIdAsync(command.Id, ct);
        if (brewMethod is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Brew method not found.");

        repository.Remove(brewMethod);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateBrewMethodHandler.InvalidateBrewMethodCachesAsync(cacheService, ct);

        return Response.Success(message: "Brew method deleted.");
    }
}
