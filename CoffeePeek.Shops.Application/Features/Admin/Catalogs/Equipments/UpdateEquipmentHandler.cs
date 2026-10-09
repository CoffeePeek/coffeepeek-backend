using System.Net;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Equipments;

public static class UpdateEquipmentHandler
{
    public static async Task<Response<EquipmentDto>> Handle(
        UpdateEquipmentCommand command,
        IEquipmentRepository repository,
        IMapper mapper,
        CancellationToken ct)
    {
        var equipment = await repository.GetByIdAsync(command.Id, ct);
        if (equipment is null)
            return Response<EquipmentDto>.Error(HttpStatusCode.NotFound, "Equipment not found.");

        var category = await repository.GetCategoryByIdAsync((int)command.Category, ct);
        if (category is null)
            return Response<EquipmentDto>.Error(HttpStatusCode.BadRequest, "Invalid equipment category.");

        if (equipment.CategoryId != category.Id &&
            await repository.HasShopAssignmentsAsync(equipment.Id, ct))
            return Response<EquipmentDto>.Error(HttpStatusCode.Conflict,
                "Equipment assigned to a shop cannot change category. Remove shop assignments first.");

        equipment.Update(command.Brand, command.ModelName, category);
        return Response<EquipmentDto>.Success(mapper.Map<EquipmentDto>(equipment));
    }
}
