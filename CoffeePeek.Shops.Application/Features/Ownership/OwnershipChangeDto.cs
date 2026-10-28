using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public record OwnershipChangeDto(Guid Id, BusinessType BusinessType, Guid BusinessId,
    Guid? PreviousOwnerUserId, Guid? NewOwnerUserId, Guid ActorUserId, string? Reason,
    Guid? ClaimId, DateTime CreatedAtUtc);
