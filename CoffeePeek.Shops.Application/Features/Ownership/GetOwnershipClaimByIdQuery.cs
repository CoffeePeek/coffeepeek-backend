namespace CoffeePeek.Shops.Application.Features.Ownership;

public record GetOwnershipClaimByIdQuery(Guid ClaimId, Guid? ApplicantUserId);
