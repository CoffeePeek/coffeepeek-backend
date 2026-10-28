using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public interface IBusinessOwnershipStore
{
    void AddClaim(BusinessOwnershipClaim claim);
    void AddChange(BusinessOwnershipChange change);
    Task<BusinessOwnershipClaim?> GetClaimAsync(Guid id, CancellationToken ct);
    Task<bool> PendingExistsAsync(BusinessType businessType, Guid businessId, Guid applicantUserId, CancellationToken ct);
    Task<BusinessOwnershipTargetDto?> GetTargetAsync(BusinessType businessType, Guid businessId, CancellationToken ct);
    Task<OwnershipClaimPage> GetClaimsAsync(GetOwnershipClaimsQuery query, CancellationToken ct);
    Task<OwnershipChangePage> GetChangesAsync(GetOwnershipChangesQuery query, CancellationToken ct);
}
