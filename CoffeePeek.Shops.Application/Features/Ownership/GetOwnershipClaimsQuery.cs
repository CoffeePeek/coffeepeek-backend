using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public record GetOwnershipClaimsQuery(Guid? ApplicantUserId, OwnershipClaimStatus? Status = null,
    BusinessType? BusinessType = null, Guid? BusinessId = null, int Page = 1, int PageSize = 20);
