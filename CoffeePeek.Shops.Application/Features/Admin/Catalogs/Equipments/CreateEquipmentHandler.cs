using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
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
        return Response<EquipmentDto>.Success(mapper.Map<EquipmentDto>(equipment));
    }
}
