using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Infrastructure.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController, Route("api/v1/profiles/{authorSlug}/shelf")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileShelfController(IProfileAddressLookup profiles, IShelfProfileStore shelf, MediaShelfBagPhotoClient media)
    : ControllerBase
{
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Get(string authorSlug, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(authorSlug, ct);
        if (profile == null) return NotFound();
        var result = await shelf.PublicAsync(profile.EntityId, profile.Slug, ct);
        // Catalog reads may await other services: rebind access to the same author before returning any cards.
        if ((await profiles.ResolveAsync(authorSlug, ct))?.EntityId != profile.EntityId) return NotFound();
        return Ok(Response<PublicShelfDto?>.Success(result));
    }

    [AllowAnonymous, HttpGet("bags/{bagId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(string authorSlug, Guid bagId, Guid photoId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var profile = await profiles.ResolveAsync(authorSlug, ct);
        var photo = profile == null ? null : await shelf.PublicPhotoAsync(profile.EntityId, bagId, photoId, ct);
        if (photo == null) return NotFound();
        HttpResponseMessage response;
        try { response = await media.DownloadPublicAsync(photo.OwnerId, bagId, photoId, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is (HttpRequestException or OperationCanceledException))
        { return StatusCode(503); }
        using var lease = response;
        if (!response.IsSuccessStatusCode) return StatusCode(response.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 503);
        var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (type is not ("image/jpeg" or "image/png" or "image/webp") || type != photo.ContentType.ToLowerInvariant()) return StatusCode(503);
        // Visibility, profile access and the current bag/photo are authoritative again after Media has responded.
        if ((await profiles.ResolveAsync(authorSlug, ct))?.EntityId != photo.OwnerId
            || await shelf.PublicPhotoAsync(photo.OwnerId, bagId, photoId, ct) == null)
            return NotFound();
        Response.ContentType = type;
        await response.Content.CopyToAsync(Response.Body, ct);
        return new EmptyResult();
    }
}
