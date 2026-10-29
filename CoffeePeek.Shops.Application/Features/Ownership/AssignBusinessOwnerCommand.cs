using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public record AssignBusinessOwnerCommand(BusinessType BusinessType, Guid BusinessId,
    Guid? OwnerUserId, Guid ActorUserId, string? Reason = null);
