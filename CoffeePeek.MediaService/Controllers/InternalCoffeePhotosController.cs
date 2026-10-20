using System.Security.Cryptography;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/coffee-photos")]
public sealed class InternalCoffeePhotosController(IStorageService storage, MediaDbContext db,
    IOptions<CoffeeImportAuthOptions> auth) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(8 * 1024 * 1024 + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024 + 65536)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (auth.Value.Token.Length < 32 || !GatewayAuthVerifier.IsTrusted(Request.Headers[CoffeeImportAuthOptions.Header], auth.Value.Token)) return Unauthorized();
        if (file is null || file.Length is <= 0 or > 8388608) return BadRequest("Photo must contain 1 byte–8 MiB.");
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var format = CoffeePhotoFormat.Detect(bytes);
        if (format is null || format.Value.ContentType != file.ContentType) return BadRequest("Unsupported image or mismatched content type.");
        var digest = SHA256.HashData(bytes);
        var hash = Convert.ToHexStringLower(digest);
        var key = $"coffees/{hash}.{format.Value.Extension}";
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.StorageKey == key, ct);
        if (await storage.GetObjectSize(key, BucketType.Shop, ct) is null)
        {
            buffer.Position = 0;
            await storage.Put(key, format.Value.ContentType, buffer, bytes.Length, BucketType.Shop, ct);
        }
        // ponytail: retain shared objects; add reference-based GC when unused images cause material storage growth.
        await storage.MarkAsPermanent(key, BucketType.Shop, ct);
        if (photo is null)
        {
            photo = new PhotoMetadata { Id = new Guid(digest.AsSpan(0, 16)), FileName = hash + "." + format.Value.Extension,
                ContentType = format.Value.ContentType, StorageKey = key, SizeBytes = bytes.Length,
                BucketType = BucketType.Shop, OwnerType = OwnerType.Coffee, OwnerId = Guid.Empty,
                UploadedAt = DateTime.UtcNow, Status = PhotoStatus.Confirmed };
            db.Photos.Add(photo);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Another daily import may upload the same content. Only accept the same stored hash.
                db.ChangeTracker.Clear();
                photo = await db.Photos.SingleOrDefaultAsync(p => p.StorageKey == key, ct);
                if (photo is null) throw;
            }
        }
        return Ok(new StoredCoffeePhotoDto(photo.Id, key, photo.FileName, photo.ContentType, photo.SizeBytes));
    }
}
