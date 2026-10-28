using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Application.Features.Ownership;

public static class OwnershipClaimHandler
{
    public static async Task<Response<OwnershipClaimDto>> Handle(SubmitOwnershipClaimCommand command,
        IBusinessOwnershipStore store, IPublicAddressService addresses, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (!Enum.IsDefined(command.BusinessType) || command.ApplicantUserId == Guid.Empty)
            throw new ValidationException("A valid business type and applicant are required.");
        if (string.IsNullOrWhiteSpace(command.BusinessSlug) || command.BusinessSlug.Length > 100)
            throw new ValidationException("A public business slug of at most 100 characters is required.");
        var kind = command.BusinessType == BusinessType.CoffeeShop ? PublicAddressKind.CoffeeShop : PublicAddressKind.Roaster;
        var address = await addresses.ResolveAsync(kind, command.BusinessSlug, ct)
            ?? throw new NotFoundException("Business not found.");
        var target = await store.GetTargetAsync(command.BusinessType, address.EntityId, ct);
        if (target is null || target.IsHidden) throw new NotFoundException("Business not found.");
        var claim = BusinessOwnershipClaim.Create(command.BusinessType, target.Id, target.Name,
            target.Slug ?? command.BusinessSlug, command.ApplicantUserId, command.Contact, command.Evidence);
        if (target.OwnerUserId.HasValue)
            throw new ConflictException("This business already has an owner.");
        if (await store.PendingExistsAsync(command.BusinessType, target.Id, command.ApplicantUserId, ct))
            throw new ConflictException("You already have a pending claim for this business.");
        store.AddClaim(claim);
        return Response<OwnershipClaimDto>.Success(OwnershipClaimDto.From(claim, null, admin: false));
    }

    public static async Task<Response<OwnershipClaimDto>> Handle(ReviewOwnershipClaimCommand command,
        IBusinessOwnershipStore store, BusinessOwnershipService ownership, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var claim = await store.GetClaimAsync(command.ClaimId, ct)
            ?? throw new NotFoundException("Ownership claim not found.");
        // A repeated decision never assigns again: an old approval cannot recreate a revoked grant.
        var changed = claim.ValidateReview(command.Approve, command.ActorUserId, command.Reason);
        var target = changed && command.Approve
            ? await ownership.AssignAsync(claim.BusinessType, claim.BusinessId, claim.ApplicantUserId,
                command.ActorUserId, command.Reason, requireUnowned: true, claim.Id, ct)
            : await store.GetTargetAsync(claim.BusinessType, claim.BusinessId, ct);
        if (changed) claim.Review(command.Approve, command.ActorUserId, command.Reason);
        return Response<OwnershipClaimDto>.Success(OwnershipClaimDto.From(claim, target?.OwnerUserId, admin: true));
    }

    public static async Task<Response<OwnershipClaimDto>> Handle(GetOwnershipClaimByIdQuery query,
        IBusinessOwnershipStore store, CancellationToken ct)
    {
        var claim = await store.GetClaimAsync(query.ClaimId, ct);
        if (claim is null || query.ApplicantUserId.HasValue && claim.ApplicantUserId != query.ApplicantUserId.Value)
            throw new NotFoundException("Ownership claim not found.");
        var target = await store.GetTargetAsync(claim.BusinessType, claim.BusinessId, ct);
        return Response<OwnershipClaimDto>.Success(OwnershipClaimDto.From(claim, target?.OwnerUserId,
            admin: query.ApplicantUserId is null));
    }

    public static async Task<Response<OwnershipClaimPage>> Handle(GetOwnershipClaimsQuery query,
        IBusinessOwnershipStore store, CancellationToken ct)
    {
        ValidatePage(query.BusinessType, query.Page, query.PageSize);
        if (query.Status.HasValue && !Enum.IsDefined(query.Status.Value))
            throw new ValidationException("Unknown ownership claim status.");
        return Response<OwnershipClaimPage>.Success(await store.GetClaimsAsync(query, ct));
    }

    public static async Task<Response<OwnershipChangePage>> Handle(GetOwnershipChangesQuery query,
        IBusinessOwnershipStore store, CancellationToken ct)
    {
        ValidatePage(query.BusinessType, query.Page, query.PageSize);
        return Response<OwnershipChangePage>.Success(await store.GetChangesAsync(query, ct));
    }

    public static async Task<Response<BusinessOwnershipTargetDto>> Handle(AssignBusinessOwnerCommand command,
        BusinessOwnershipService ownership, IUnitOfWork unitOfWork, CancellationToken ct) =>
        Response<BusinessOwnershipTargetDto>.Success(await ownership.AssignAsync(command.BusinessType,
            command.BusinessId, command.OwnerUserId, command.ActorUserId, command.Reason, false, null, ct));

    private static void ValidatePage(BusinessType? businessType, int page, int pageSize)
    {
        if (businessType.HasValue && !Enum.IsDefined(businessType.Value))
            throw new ValidationException("Unknown business type.");
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ValidationException("Page must be positive and pageSize must be between 1 and 100.");
    }
}
