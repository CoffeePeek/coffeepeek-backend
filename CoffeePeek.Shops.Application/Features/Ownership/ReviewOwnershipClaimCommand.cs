namespace CoffeePeek.Shops.Application.Features.Ownership;

public record ReviewOwnershipClaimCommand(Guid ClaimId, Guid ActorUserId, bool Approve, string? Reason = null);
