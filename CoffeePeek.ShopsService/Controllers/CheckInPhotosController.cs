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
    public async Task<IActionResult> Download(Guid id, Guid photoId, CancellationToken ct)
    {
        var readerId = user.GetUserId();
        var shop = await queries.GetUnifiedByIdAsync(id, readerId, ct);
        if (shop != null)
            return await StreamAsync(shop.Id, shop.UserId, shop.Photos, shop.DrinkPhoto, photoId, ct);
        if (readerId == null) return NotFound();
        var home = await queries.GetHomeByIdAsync(id, readerId.Value, ct);
        return home == null ? NotFound() : await StreamAsync(home.Id, readerId.Value,
            home.Photos, home.DrinkPhoto, photoId, ct);
    }

    [HttpGet("{id:guid}/moderation-photos/{photoId:guid}")]
    [Authorize(Policy = RoleConsts.Moderator)]
    public async Task<IActionResult> DownloadModeration(Guid id, Guid photoId, [FromQuery] int revision, CancellationToken ct)
    {
        var shop = await queries.GetModerationSnapshotAsync(id, revision, ct);
        return shop == null ? NotFound() : await StreamAsync(shop.Id, shop.UserId,
            shop.Photos, shop.DrinkPhoto, photoId, ct);
    }

    private async Task<IActionResult> StreamAsync(Guid checkInId, Guid ownerId, CheckInPhotoDto[] photos,
        CheckInPhotoDto? drinkPhoto, Guid photoId, CancellationToken ct)
    {
        var photo = drinkPhoto?.Id == photoId ? drinkPhoto : photos.FirstOrDefault(p => p.Id == photoId);
        if (photo == null) return NotFound();
        using var response = await media.DownloadAsync(ownerId, checkInId, photoId, ct);
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
