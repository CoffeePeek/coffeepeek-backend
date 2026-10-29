using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Ownership;

public sealed class BusinessOwnershipStore(ShopsDbContext db) : IBusinessOwnershipStore
{
    public void AddClaim(BusinessOwnershipClaim claim) => db.BusinessOwnershipClaims.Add(claim);
    public void AddChange(BusinessOwnershipChange change) => db.BusinessOwnershipChanges.Add(change);

    public Task<BusinessOwnershipClaim?> GetClaimAsync(Guid id, CancellationToken ct) =>
        db.BusinessOwnershipClaims.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> PendingExistsAsync(BusinessType businessType, Guid businessId, Guid applicantUserId, CancellationToken ct) =>
        db.BusinessOwnershipClaims.AnyAsync(x => x.BusinessType == businessType && x.BusinessId == businessId
            && x.ApplicantUserId == applicantUserId && x.Status == OwnershipClaimStatus.Pending, ct);

    public Task<BusinessOwnershipClaim?> GetLatestClaimAsync(BusinessType businessType, Guid businessId, Guid applicantUserId, CancellationToken ct) =>
        db.BusinessOwnershipClaims.AsNoTracking().Where(x => x.BusinessType == businessType && x.BusinessId == businessId
                && x.ApplicantUserId == applicantUserId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    public Task<BusinessOwnershipTargetDto?> GetTargetAsync(BusinessType businessType, Guid businessId, CancellationToken ct) =>
        businessType switch
        {
            BusinessType.CoffeeShop => db.Shops.AsNoTracking().Where(x => x.Id == businessId)
                .Select(x => new BusinessOwnershipTargetDto(BusinessType.CoffeeShop, x.Id, x.Name, x.PublicSlug, x.OwnerUserId, x.IsHidden))
                .FirstOrDefaultAsync(ct),
            BusinessType.Roaster => db.Roasters.AsNoTracking().Where(x => x.Id == businessId)
                .Select(x => new BusinessOwnershipTargetDto(BusinessType.Roaster, x.Id, x.Name, x.PublicSlug, x.OwnerUserId, false))
                .FirstOrDefaultAsync(ct),
            _ => throw new ValidationException("Unknown business type.")
        };

    public async Task<OwnershipClaimPage> GetClaimsAsync(GetOwnershipClaimsQuery request, CancellationToken ct)
    {
        var query = db.BusinessOwnershipClaims.AsNoTracking();
        if (request.ApplicantUserId.HasValue) query = query.Where(x => x.ApplicantUserId == request.ApplicantUserId.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (request.BusinessType.HasValue) query = query.Where(x => x.BusinessType == request.BusinessType.Value);
        if (request.BusinessId.HasValue) query = query.Where(x => x.BusinessId == request.BusinessId.Value);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new
            {
                Claim = x,
                Owner = x.BusinessType == BusinessType.CoffeeShop
                    ? db.Shops.Where(s => s.Id == x.BusinessId).Select(s => s.OwnerUserId).FirstOrDefault()
                    : db.Roasters.Where(r => r.Id == x.BusinessId).Select(r => r.OwnerUserId).FirstOrDefault()
            }).ToArrayAsync(ct);
        var items = rows.Select(x => OwnershipClaimDto.From(x.Claim, x.Owner, request.ApplicantUserId is null)).ToArray();
        return new(items, total, request.Page, request.PageSize);
    }

    public async Task<OwnershipChangePage> GetChangesAsync(GetOwnershipChangesQuery request, CancellationToken ct)
    {
        var query = db.BusinessOwnershipChanges.AsNoTracking();
        if (request.BusinessType.HasValue) query = query.Where(x => x.BusinessType == request.BusinessType.Value);
        if (request.BusinessId.HasValue) query = query.Where(x => x.BusinessId == request.BusinessId.Value);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new OwnershipChangeDto(x.Id, x.BusinessType, x.BusinessId, x.PreviousOwnerUserId,
                x.NewOwnerUserId, x.ActorUserId, x.Reason, x.ClaimId, x.CreatedAtUtc)).ToArrayAsync(ct);
        return new(items, total, request.Page, request.PageSize);
    }
}
