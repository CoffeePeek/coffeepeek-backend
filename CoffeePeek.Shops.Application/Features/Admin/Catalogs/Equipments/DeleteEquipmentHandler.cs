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

public static class DeleteEquipmentHandler
{
    public static async Task<Response> Handle(
        DeleteEquipmentCommand command,
        IEquipmentRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var equipment = await repository.GetByIdAsync(command.Id, ct);
        if (equipment is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Equipment not found.");

        repository.Remove(equipment);
        await unitOfWork.SaveChangesAsync(ct);
        await CreateEquipmentHandler.InvalidateEquipmentCachesAsync(cacheService, ct);

        return Response.Success(message: "Equipment deleted.");
    }
}
