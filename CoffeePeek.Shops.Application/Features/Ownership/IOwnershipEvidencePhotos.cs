namespace CoffeePeek.Shops.Application.Features.Ownership;
public interface IOwnershipEvidencePhotos
{
    Task AttachAsync(Guid applicantId, Guid claimId, Guid[] photoIds, CancellationToken ct);
}
