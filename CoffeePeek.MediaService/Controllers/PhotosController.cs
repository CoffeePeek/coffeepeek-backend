using System.ComponentModel;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Commands.Base;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.MediaService.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status500InternalServerError)]
public class PhotosController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Generate presigned upload url for avatar photo
    /// </summary>
    [HttpPost("avatar")]
    [Description("Get url for presigned upload avatar photo")]
    [ProducesResponseType<Response<GenerateUploadUrlResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateUploadUrl([FromBody] GenerateAvatarUploadCommand command, CancellationToken ct)
    {
        command = command with { OwnerId = userContext.GetUserIdOrThrow() };
        return Ok(await bus.InvokeAsync<Response<GenerateUploadUrlResponse>>(command, ct));
    }

    /// <summary>
    /// Generate presigned upload urls for shop photos
    /// </summary>
    [HttpPost("shop")]
    [Description("Get urls for presigned upload photos")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>(StatusCodes.Status200OK)]
    public Task<IActionResult> GenerateUploadUrls([FromBody] List<PhotoRequest> requests, CancellationToken ct) =>
        Generate(requests, null, BucketType.Shop, ct);

    /// <summary>
    /// Generate presigned upload urls for menu photos (shop bucket, prefix menus/).
    /// </summary>
    [HttpPost("menu")]
    [Description("Get urls for presigned upload menu photos")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>(StatusCodes.Status200OK)]
    public Task<IActionResult> GenerateMenuUploadUrls([FromBody] List<PhotoRequest> requests, CancellationToken ct) =>
        Generate(requests, "menus", BucketType.Shop, ct);

    /// <summary>
    /// Generate presigned upload urls for roaster photos (shop bucket, prefix roasters/).
    /// </summary>
    [HttpPost("roaster")]
    [Description("Get urls for presigned upload roaster photos")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>(StatusCodes.Status200OK)]
    public Task<IActionResult> GenerateRoasterUploadUrls([FromBody] List<PhotoRequest> requests, CancellationToken ct) =>
        Generate(requests, "roasters", BucketType.Shop, ct);

    /// <summary>
    /// Generate presigned upload urls for check-in photos in the private check-in bucket.
    /// </summary>
    [HttpPost("check-in")]
    [Description("Get urls for presigned upload check-in photos")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>(StatusCodes.Status200OK)]
    public Task<IActionResult> GenerateCheckInUploadUrls([FromBody] List<PhotoRequest> requests, CancellationToken ct) =>
        Generate(requests, "check-ins", BucketType.CheckIn, ct);

    /// <summary>
    /// Generate one presigned upload url for a shelf-bag card in the private check-in bucket (prefix shelf-bags/).
    /// Exactly one JPEG, PNG or WebP of at most 1 MiB; Media validates the bytes when Shops claims the upload.
    /// </summary>
    [HttpPost("shelf-bag")]
    [Description("Get url for presigned upload of a shelf-bag card")]
    [ProducesResponseType<Response<List<GenerateUploadUrlResponse>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateShelfBagUploadUrl([FromBody] List<PhotoRequest> requests, CancellationToken ct)
    {
        if (requests is not [{ } request] || !ShelfBagPhotoValidation.IsAllowedContentType(request.ContentType)
            || request.SizeBytes is <= 0 or > ShelfBagPhotoValidation.MaxSizeBytes
            || string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Length > 255)
            return BadRequest(new ErrorResponse("Send exactly one JPEG, PNG or WebP card of at most 1 MiB."));
        return await Generate(requests, "shelf-bags", BucketType.CheckIn, ct);
    }

    /// <summary>
    /// Resolve trusted metadata for photos the caller uploaded and has not attached yet.
    /// Service-to-service endpoint (used by Shops); always scoped to the authenticated caller,
    /// so keys owned by other users, already used, expired or never uploaded are omitted.
    /// </summary>
    [HttpPost("resolve")]
    [Description("Resolve the caller's pending uploaded photos by storage key")]
    [ProducesResponseType<Response<List<ResolvedUploadedPhotoDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveUploadedPhotos([FromBody] ResolveUploadedPhotosQuery query, CancellationToken ct)
    {
        query = query with { OwnerId = userContext.GetUserIdOrThrow() };
        return Ok(await bus.InvokeAsync<Response<List<ResolvedUploadedPhotoDto>>>(query, ct));
    }

    private async Task<IActionResult> Generate(List<PhotoRequest> requests, string? prefix, BucketType bucket, CancellationToken ct)
    {
        var command = new GenerateShopPhotosCommand(requests, userContext.GetUserIdOrThrow(), prefix, bucket);
        return Ok(await bus.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(command, ct));
    }
}
