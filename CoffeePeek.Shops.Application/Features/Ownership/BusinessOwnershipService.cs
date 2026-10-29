using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public sealed class BusinessOwnershipService(IBusinessOwnershipStore store, ICoffeeShopRepository shops,
    IRoasterOwnershipWriter roasters, IUserExistenceLookup users, IMessageBus bus)
{
    public async Task<BusinessOwnershipTargetDto> AssignAsync(BusinessType businessType, Guid businessId,
        Guid? ownerUserId, Guid actorUserId, string? reason, bool requireUnowned, Guid? claimId, CancellationToken ct)
    {
        if (!Enum.IsDefined(businessType) || businessId == Guid.Empty || actorUserId == Guid.Empty)
            throw new ValidationException("Valid business and administrator are required.");
        ownerUserId = ownerUserId == Guid.Empty ? null : ownerUserId;
        reason = BusinessOwnershipClaim.NormalizeReason(reason, required: false);
        var shop = businessType == BusinessType.CoffeeShop
            ? await shops.GetByIdAsync(businessId, ct) ?? throw new NotFoundException("Business not found.")
            : null;
        var target = shop is not null
            ? new BusinessOwnershipTargetDto(businessType, shop.Id, shop.Name, shop.PublicSlug, shop.OwnerUserId, shop.IsHidden)
            : await store.GetTargetAsync(businessType, businessId, ct) ?? throw new NotFoundException("Business not found.");
        if (shop is not null)
        {
            if (requireUnowned && (shop.OwnerUserId.HasValue || shop.IsHidden))
                throw new ConflictException("The business is no longer available for ownership approval.");
            if (shop.OwnerUserId == ownerUserId) return target;
        }
        if (ownerUserId is { } owner && !await users.ExistsAsync(owner, ct))
            throw new ConflictException("The applicant account could not be confirmed; retry later.");
        if (ownerUserId is null)
            reason = businessType == BusinessType.Roaster
                ? BusinessOwnershipClaim.NormalizeReason(reason, required: true)
                : reason ?? "Access revoked through the legacy shop assignment endpoint.";

        Guid? previous;
        if (shop is not null)
        {
            previous = shop.OwnerUserId;
            shop.AssignOwner(ownerUserId);
            shop.AdvanceProfileRevision();
            await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        }
        else
        {
            previous = await roasters.AssignAsync(businessId, ownerUserId, requireUnowned, ct);
            if (previous == ownerUserId) return target with { OwnerUserId = previous };
        }

        store.AddChange(BusinessOwnershipChange.Create(businessType, businessId, previous,
            ownerUserId, actorUserId, reason, claimId));
        return target with { OwnerUserId = ownerUserId };
    }
}
