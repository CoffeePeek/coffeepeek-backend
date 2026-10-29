namespace CoffeePeek.Contract.Dtos.Ownership;
public sealed record AttachOwnershipEvidenceRequest(Guid ClaimId, Guid[] PhotoIds, Guid? PreviousRejectedClaimId = null);
