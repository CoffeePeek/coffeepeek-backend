using CoffeePeek.Shared.Auth;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Infrastructure.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController, Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CollectionCoversController(ISavedCollectionsStore collections, MediaCollectionCoverClient media, IUserContext user) : ControllerBase
{
    [AllowAnonymous, HttpGet("api/v1/favorite-collections/{id:guid}/cover/{photoId:guid}")]
    public async Task<IActionResult> Public(Guid id, Guid photoId, CancellationToken ct) =>
        await DownloadAsync(await collections.CoverAsync(null, id, photoId, ct), ct);

    [HttpGet("api/v1/saved-collections/{id:guid}/cover/{photoId:guid}")]
    public async Task<IActionResult> Owned(Guid id, Guid photoId, CancellationToken ct) =>
        await DownloadAsync(await collections.CoverAsync(user.GetUserIdOrThrow(), id, photoId, ct), ct);

    private async Task<IActionResult> DownloadAsync(CollectionCoverAccess? photo, CancellationToken ct)
    {
        if (photo == null) return NotFound();
        using var response = await media.DownloadAsync(photo, ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode(response.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 503);
        var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (type is not ("image/jpeg" or "image/png" or "image/webp") || type != photo.ContentType.ToLowerInvariant())
            return StatusCode(503);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = type;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await stream.CopyToAsync(Response.Body, ct);
        return new EmptyResult();
    }
}
