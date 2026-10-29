using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Authorize]
[Route("api/Photos/ownership-evidence")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OwnershipEvidenceUploadsController(IUserContext user, IPhotoRepository photos,
    IStorageService storage, IUnitOfWork unitOfWork) : ControllerBase
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;
    [HttpPost]
    [RequestSizeLimit(MaxPhotoBytes + 65536)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, CancellationToken ct)
    {
        var ownerId = user.GetUserIdOrThrow();
        if (file.Length is <= 0 or > MaxPhotoBytes || !GenerateShopPhotoHandler.IsSafePhotoContentType(file.ContentType))
            return BadRequest(new ErrorResponse("A photo of at most 5 MiB is required."));
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (!OwnershipEvidenceImage.IsValid(bytes) || Path.GetFileName(file.FileName).Length > 255)
            return BadRequest(new ErrorResponse("A valid photo file is required."));
        await storage.EnsureCheckInBucketPrivateAsync(ct);
        var id = Guid.NewGuid();
        var key = $"ownership-evidence/{id:N}";
        var type = file.ContentType.ToLowerInvariant();
        await using var input = new MemoryStream(bytes, writable: false);
        await storage.Put(key, type, input, file.Length, BucketType.CheckIn, ct);
        await storage.MarkAsTemporary(key, BucketType.CheckIn, ct);
        var metadata = PhotoMetadata.Pending(Path.GetFileName(file.FileName), type, key,
            file.Length, BucketType.CheckIn, OwnerType.OwnershipEvidence, ownerId);
        photos.Add(metadata);
        await unitOfWork.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(Response<GenerateUploadUrlResponse>.Success(new(metadata.Id, "", metadata.StorageKey)));
    }
}
