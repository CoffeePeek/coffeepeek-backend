using CoffeePeek.Contract.Dtos.Ownership;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Route("internal/ownership-evidence")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InternalOwnershipEvidenceController(IPhotoRepository photos, IStorageService storage,
    IUnitOfWork unitOfWork, IUserContext user, IOptions<GatewayAuthOptions> auth) : ControllerBase
{
    private bool Trusted => GatewayAuthVerifier.IsTrusted(Request.Headers[GatewayHeaderConsts.XGatewayAuth], auth.Value.SecretKey);
    [HttpPost("attach")]
    public async Task<IActionResult> Attach(AttachOwnershipEvidenceRequest request, CancellationToken ct)
    {
        if (!Trusted || user.GetUserId() is not { } owner) return Unauthorized();
        if (request.ClaimId == Guid.Empty || request.PhotoIds is not { Length: > 0 and <= 3 }
            || request.PhotoIds.Distinct().Count() != request.PhotoIds.Length
            || request.PreviousRejectedClaimId == Guid.Empty || request.PreviousRejectedClaimId == request.ClaimId) return BadRequest();
        var candidates = new List<PhotoMetadata>();
        foreach (var id in request.PhotoIds)
        {
            var photo = await photos.GetByIdAsync(id, ct);
            if (photo == null || photo.OwnerId != owner || photo.OwnerType != OwnerType.OwnershipEvidence
                || photo.BucketType != BucketType.CheckIn) return BadRequest();
            candidates.Add(photo);
        }
        await using var locks = await photos.AcquireClaimLocksAsync(owner, candidates.Select(p => p.StorageKey).ToArray(), ct);
        candidates = await photos.GetClaimPhotosAsync(owner, candidates.Select(p => p.StorageKey).ToArray(), ct);
        if (candidates.Count != request.PhotoIds.Length || candidates.Any(p => p.OwnerId != owner
            || p.OwnerType != OwnerType.OwnershipEvidence || p.BucketType != BucketType.CheckIn
            || p.Status is not (PhotoStatus.Pending or PhotoStatus.Confirmed)
            || p.CheckInId != null || p.ShopAttachmentId != null || p.ShelfBagId != null)) return Conflict();
        foreach (var photo in candidates)
        {
            if (!await CanAttachAsync(photo, request, ct)) return Conflict();
            var size = await storage.GetObjectSize(photo.StorageKey, BucketType.CheckIn, ct);
            if (size is <= 0 or > OwnershipEvidenceUploadsController.MaxPhotoBytes || size == null) return BadRequest();
        }
        foreach (var photo in candidates)
        {
            photo.OwnershipClaimId ??= request.ClaimId;
            if (!await photos.HasEvidenceBindingAsync(photo.Id, request.ClaimId, ct))
                photos.AddEvidenceBinding(photo.Id, request.ClaimId);
            photo.Status = PhotoStatus.Confirmed;
            photo.ClaimedAtUtc = DateTime.UtcNow;
            await storage.MarkAsPermanent(photo.StorageKey, BucketType.CheckIn, ct);
        }
        await unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{claimId:guid}/{photoId:guid}")]
    public async Task<IActionResult> Download(Guid claimId, Guid photoId, CancellationToken ct)
    {
        if (!Trusted || user.GetUserId() is not { } owner) return Unauthorized();
        var photo = await photos.GetByIdAsync(photoId, ct);
        if (photo == null || photo.OwnerId != owner
            || photo.OwnerType != OwnerType.OwnershipEvidence || photo.BucketType != BucketType.CheckIn
            || photo.Status != PhotoStatus.Confirmed) return NotFound();
        if (photo.OwnershipClaimId != claimId && !await photos.HasEvidenceBindingAsync(photoId, claimId, ct)) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = photo.ContentType;
        await storage.CopyToAsync(photo.StorageKey, BucketType.CheckIn, Response.Body, ct);
        return new EmptyResult();
    }

    private async Task<bool> CanAttachAsync(PhotoMetadata photo, AttachOwnershipEvidenceRequest request, CancellationToken ct)
    {
        if (photo.OwnershipClaimId == null) return photo.Status == PhotoStatus.Pending && !photo.IsExpired;
        if (photo.OwnershipClaimId == request.ClaimId) return true;
        if (photo.Status != PhotoStatus.Confirmed) return false;
        if (await photos.HasEvidenceBindingAsync(photo.Id, request.ClaimId, ct)) return true;
        return request.PreviousRejectedClaimId is { } previous
            && (photo.OwnershipClaimId == previous || await photos.HasEvidenceBindingAsync(photo.Id, previous, ct));
    }
}
