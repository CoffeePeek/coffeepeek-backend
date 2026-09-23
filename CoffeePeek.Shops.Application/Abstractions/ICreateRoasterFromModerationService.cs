using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Shops.Application.Abstractions;

public interface ICreateRoasterFromModerationService
{
    Task<Guid> CreateRoasterFromApprovedEventAsync(
        ModerationRoasterDto roasterDto, Guid moderationId, CancellationToken ct = default);
}
