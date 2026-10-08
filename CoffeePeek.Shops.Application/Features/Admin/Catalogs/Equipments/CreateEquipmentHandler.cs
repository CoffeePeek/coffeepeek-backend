using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Equipments;

public static class CreateEquipmentHandler
{
    public static async Task<Response<EquipmentDto>> Handle(
        CreateEquipmentCommand command,
        IEquipmentRepository repository,
        IMapper mapper,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var category = await repository.GetCategoryByIdAsync((int)command.Category, ct);
        if (category is null)
            return Response<EquipmentDto>.Error(HttpStatusCode.BadRequest, "Invalid equipment category.");

        var existing = await repository.GetByBrandAndModelAsync(command.Brand, command.ModelName, ct);
        if (existing is not null)
            return Response<EquipmentDto>.Error(HttpStatusCode.Conflict, "Equipment with this brand and model already exists.");

        var equipment = new Equipment(command.Brand, command.ModelName, category);
        repository.Add(equipment);
        await unitOfWork.SaveChangesAsync(ct);
        await InvalidateEquipmentCachesAsync(cacheService, ct);

        return Response<EquipmentDto>.Success(mapper.Map<EquipmentDto>(equipment));
    }

    internal static async Task InvalidateEquipmentCachesAsync(ICacheService cacheService, CancellationToken ct)
    {
        await cacheService.RemoveByPattern(CacheKey.Equipment.ListPattern(), ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
    }
}
