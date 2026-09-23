using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;

public record CreateCoffeeBeanCommand(string Name);

public static class CreateCoffeeBeanHandler
{
    public static async Task<Response<CoffeeBeansDto>> Handle(
        CreateCoffeeBeanCommand command,
        ICoffeeBeanRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var existing = await repository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            return Response<CoffeeBeansDto>.Error(HttpStatusCode.Conflict, "A coffee bean with this name already exists.");

        var bean = new CoffeeBean(command.Name);
        repository.Add(bean);
        await unitOfWork.SaveChangesAsync(ct);
        await InvalidateCoffeeBeanCachesAsync(cacheService, ct);

        return Response<CoffeeBeansDto>.Success(mapper.Map<CoffeeBeansDto>(bean));
    }

    internal static async Task InvalidateCoffeeBeanCachesAsync(ICacheService cacheService, CancellationToken ct)
    {
        await cacheService.RemoveByPattern(CacheKey.CoffeeBean.ListPattern(), ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }
}

public record UpdateCoffeeBeanCommand([property: JsonIgnore] Guid Id, string Name);

public static class UpdateCoffeeBeanHandler
{
    public static async Task<Response<CoffeeBeansDto>> Handle(
        UpdateCoffeeBeanCommand command,
        ICoffeeBeanRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var bean = await repository.GetByIdAsync(command.Id, ct);
        if (bean is null)
            return Response<CoffeeBeansDto>.Error(HttpStatusCode.NotFound, "Coffee bean not found.");

        bean.Update(command.Name);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateCoffeeBeanHandler.InvalidateCoffeeBeanCachesAsync(cacheService, ct);

        return Response<CoffeeBeansDto>.Success(mapper.Map<CoffeeBeansDto>(bean));
    }
}

public record DeleteCoffeeBeanCommand([property: JsonIgnore] Guid Id);

public static class DeleteCoffeeBeanHandler
{
    public static async Task<Response> Handle(
        DeleteCoffeeBeanCommand command,
        ICoffeeBeanRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var bean = await repository.GetByIdAsync(command.Id, ct);
        if (bean is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Coffee bean not found.");

        repository.Remove(bean);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateCoffeeBeanHandler.InvalidateCoffeeBeanCachesAsync(cacheService, ct);

        return Response.Success(message: "Coffee bean deleted.");
    }
}
