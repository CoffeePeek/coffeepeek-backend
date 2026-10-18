using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Shared.Kernel.Observability;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/v1/check-ins")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CheckInPhotosController(ICheckInPhotoAccessQueries queries, MediaCheckInPhotoClient media, IUserContext user) : ControllerBase
{
    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(Guid id, Guid photoId, CancellationToken ct)
    {
        var readerId = user.GetUserId();
        return await DownloadAsync(() => queries.GetAsync(id, photoId, readerId, ct),
            readerId.HasValue ? "authenticated" : "anonymous", ct);
    }

    [HttpGet("{id:guid}/moderation-photos/{photoId:guid}")]
    [Authorize(Policy = RoleConsts.Moderator)]
    public async Task<IActionResult> DownloadModeration(Guid id, Guid photoId, [FromQuery] int revision, CancellationToken ct)
    {
        return await DownloadAsync(() => queries.GetModerationAsync(id, photoId, revision, ct), "moderator", ct);
    }

    private async Task<IActionResult> DownloadAsync(Func<Task<CheckInPhotoAccess?>> authorize, string reader, CancellationToken ct)
    {
        using var measurement = PhotoDeliveryTelemetry.Start("download", reader);
        var status = 500;
        try
        {
            var result = await StreamAsync(await authorize(), ct);
            status = result is StatusCodeResult code ? code.StatusCode : Response.StatusCode;
            measurement.Complete(status == 200 ? "complete" : status == 404 ? "denied" : "unavailable");
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            status = 499;
            measurement.Complete("cancelled");
            throw;
        }
        catch (ServiceUnavailableException)
        {
            status = 503;
            measurement.Complete("unavailable");
            throw;
        }
        finally { PhotoDeliveryTelemetry.Response(reader, status); }
    }

    private async Task<IActionResult> StreamAsync(CheckInPhotoAccess? photo, CancellationToken ct)
    {
        if (photo == null) return NotFound();
        using var response = await media.DownloadAsync(photo.OwnerId, photo.CheckInId, photo.PhotoId, ct);
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
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await PhotoDeliveryTelemetry.CopyAsync(source, Response.Body, "shops_stream", ct);
        return new EmptyResult();
    }

    private static bool IsSafePhotoContentType(string? contentType) => contentType?.ToLowerInvariant() is
        "image/jpeg" or "image/png" or "image/gif" or "image/webp" or "image/bmp" or "image/avif";
}
