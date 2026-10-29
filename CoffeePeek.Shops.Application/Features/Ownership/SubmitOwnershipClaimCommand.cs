using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public record SubmitOwnershipClaimCommand(Guid ApplicantUserId, BusinessType BusinessType,
    string BusinessSlug, string Contact, string Evidence);
