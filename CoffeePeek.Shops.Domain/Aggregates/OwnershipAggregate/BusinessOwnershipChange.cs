using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

public sealed class BusinessOwnershipChange : Entity<Guid>
{
    public BusinessType BusinessType { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid? PreviousOwnerUserId { get; private set; }
    public Guid? NewOwnerUserId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ClaimId { get; private set; }

    private BusinessOwnershipChange() { }

    public static BusinessOwnershipChange Create(BusinessType businessType, Guid businessId,
        Guid? previousOwnerUserId, Guid? newOwnerUserId, Guid actorUserId, string? reason, Guid? claimId = null)
    {
        if (!Enum.IsDefined(businessType) || businessId == Guid.Empty || actorUserId == Guid.Empty
            || previousOwnerUserId == Guid.Empty || newOwnerUserId == Guid.Empty || claimId == Guid.Empty)
            throw new ValidationException("Valid ownership change identities are required.");
        if (previousOwnerUserId == newOwnerUserId)
            throw new ValidationException("An ownership change must change the owner.");
        return new BusinessOwnershipChange
        {
            Id = Guid.NewGuid(), BusinessType = businessType, BusinessId = businessId,
            PreviousOwnerUserId = previousOwnerUserId, NewOwnerUserId = newOwnerUserId,
            ActorUserId = actorUserId, Reason = BusinessOwnershipClaim.NormalizeReason(reason, false),
            ClaimId = claimId, CreatedAtUtc = DateTime.UtcNow
        };
    }
}
