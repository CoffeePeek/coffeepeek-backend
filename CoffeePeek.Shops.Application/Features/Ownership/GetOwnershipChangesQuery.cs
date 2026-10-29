using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public record GetOwnershipChangesQuery(BusinessType? BusinessType = null, Guid? BusinessId = null,
    int Page = 1, int PageSize = 20);
