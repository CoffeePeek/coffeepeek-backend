using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public record BusinessOwnershipTargetDto(BusinessType BusinessType, Guid Id, string Name,
    string? Slug, Guid? OwnerUserId, bool IsHidden = false);
