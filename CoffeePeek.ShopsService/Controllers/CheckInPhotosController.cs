using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/v1/check-ins")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CheckInPhotosController(ICheckInQueries queries, MediaCheckInPhotoClient media, IUserContext user) : ControllerBase
{
    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(Guid id, Guid photoId, CancellationToken ct) =>
        await StreamAsync(await queries.GetUnifiedByIdAsync(id, user.GetUserId(), ct), photoId, ct);

    [HttpGet("{id:guid}/moderation-photos/{photoId:guid}")]
    [Authorize(Policy = RoleConsts.Moderator)]
    public async Task<IActionResult> DownloadModeration(Guid id, Guid photoId, [FromQuery] int revision, CancellationToken ct) =>
        await StreamAsync(await queries.GetModerationSnapshotAsync(id, revision, ct), photoId, ct);

    private async Task<IActionResult> StreamAsync(UnifiedCheckInDto? checkIn, Guid photoId, CancellationToken ct)
    {
        if (checkIn == null) return NotFound();
        var photo = checkIn.Photos.FirstOrDefault(p => p.Id == photoId);
        if (photo == null) return NotFound();
        using var response = await media.DownloadAsync(checkIn.UserId, checkIn.Id, photoId, ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode(response.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 503);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (IsSafePhotoContentType(photo.ContentType) && IsSafePhotoContentType(mediaType))
            Response.ContentType = mediaType!.ToLowerInvariant();
        else
        {
            Response.ContentType = "application/octet-stream";
            Response.Headers.ContentDisposition = "attachment";
        }
        await response.Content.CopyToAsync(Response.Body, ct);
        return new EmptyResult();
    }

    private static bool IsSafePhotoContentType(string? contentType) => contentType?.ToLowerInvariant() is
        "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/bmp" or "image/avif";
}
