using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public record OwnershipClaimDto(Guid Id, BusinessType BusinessType, Guid BusinessId,
    string BusinessNameSnapshot, string BusinessSlugSnapshot, Guid ApplicantUserId,
    string Contact, string Evidence, OwnershipClaimStatus Status, DateTime CreatedAtUtc,
    Guid? ReviewedByUserId, DateTime? ReviewedAtUtc, string? DecisionReason,
    bool HasCurrentAccess, Guid? CurrentOwnerUserId)
{
    public static OwnershipClaimDto From(BusinessOwnershipClaim claim, Guid? currentOwnerUserId, bool admin) =>
        new(claim.Id, claim.BusinessType, claim.BusinessId, claim.BusinessNameSnapshot,
            claim.BusinessSlugSnapshot, claim.ApplicantUserId, claim.Contact, claim.Evidence, claim.Status,
            claim.CreatedAtUtc, claim.ReviewedByUserId, claim.ReviewedAtUtc, claim.DecisionReason,
            currentOwnerUserId == claim.ApplicantUserId, admin ? currentOwnerUserId : null);
}
