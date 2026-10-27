using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Application.Features.Shelf.CreateShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.DeleteShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.FinishShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.ReorderShelf;
using CoffeePeek.Shops.Application.Features.Shelf.RestoreShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.UpdateShelfBag;
using CoffeePeek.Shops.Infrastructure.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>The signed-in user's private coffee shelf and its archive. Another owner's bag answers 404.</summary>
[ApiController]
[Authorize]
[Route("api/v1/shelf")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShelfController(IMessageBus bus, IUserContext user, IShelfQueries queries, MediaShelfBagPhotoClient media)
    : ControllerBase
{
    /// <summary>Longest accepted archive search text.</summary>
    public const int MaxArchiveQueryLength = 100;

    /// <summary>Bags on the shelf, first bag first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<ShelfBagDto[]>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(Response<ShelfBagDto[]>.Success(await queries.GetShelfAsync(user.GetUserIdOrThrow(), ct)));

    /// <summary>Finished bags, newest first; optional case-insensitive match on name or roaster.</summary>
    [HttpGet("archive")]
    [ProducesResponseType(typeof(Response<ShelfArchivePage>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? query = null, CancellationToken ct = default)
    {
        if (query?.Length > MaxArchiveQueryLength)
            return BadRequest(new ErrorResponse($"'query' must be at most {MaxArchiveQueryLength} characters."));
        pageNumber = Math.Clamp(pageNumber, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return Ok(Response<ShelfArchivePage>.Success(
            await queries.GetArchiveAsync(user.GetUserIdOrThrow(), pageNumber, pageSize, query, ct)));
    }

    /// <summary>Add a bag at the front of the shelf.</summary>
    [HttpPost("bags")]
    [ProducesResponseType(typeof(Response<ShelfBagDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateShelfBagRequest request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<ShelfBagDto>>(new CreateShelfBagCommand(user.GetUserIdOrThrow(), request), ct));

    /// <summary>Replace editable details; an omitted photo keeps the current card.</summary>
    [HttpPut("bags/{id:guid}")]
    [ProducesResponseType(typeof(Response<ShelfBagDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShelfBagRequest request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<ShelfBagDto>>(new UpdateShelfBagCommand(user.GetUserIdOrThrow(), id, request), ct));

    /// <summary>Reorder the shelf; bagIds must list every OnShelf bag exactly once, otherwise 409.</summary>
    [HttpPut("order")]
    [ProducesResponseType(typeof(Response<ShelfBagDto[]>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reorder([FromBody] ReorderShelfRequest request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<ShelfBagDto[]>>(new ReorderShelfCommand(user.GetUserIdOrThrow(), request.BagIds), ct));

    /// <summary>Move a shelf bag to the archive with an optional rating and buy-again verdict.</summary>
    [HttpPost("bags/{id:guid}/finish")]
    [ProducesResponseType(typeof(Response<ShelfBagDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Finish(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FinishShelfBagRequest? request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<ShelfBagDto>>(
            new FinishShelfBagCommand(user.GetUserIdOrThrow(), id, request?.Rating, request?.BuyAgain), ct));

    /// <summary>Return an archived bag to the shelf at an optional position (default 0).</summary>
    [HttpPost("bags/{id:guid}/restore")]
    [ProducesResponseType(typeof(Response<ShelfBagDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RestoreShelfBagRequest? request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<ShelfBagDto>>(
            new RestoreShelfBagCommand(user.GetUserIdOrThrow(), id, request?.Position), ct));

    /// <summary>Delete a mistaken bag from the shelf or the archive; its card becomes unreadable.</summary>
    [HttpDelete("bags/{id:guid}")]
    [ProducesResponseType(typeof(Response), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response>(new DeleteShelfBagCommand(user.GetUserIdOrThrow(), id), ct));

    /// <summary>Owner-only card bytes, streamed from Media.</summary>
    [HttpGet("bags/{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var photo = await queries.GetPhotoAsync(user.GetUserIdOrThrow(), id, photoId, ct);
        if (photo == null) return NotFound();
        using var response = await media.DownloadAsync(photo.OwnerId, photo.BagId, photo.PhotoId, ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode(response.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 503);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (IsCardContentType(photo.ContentType) && IsCardContentType(mediaType))
            Response.ContentType = mediaType!;
        else
        {
            Response.ContentType = "application/octet-stream";
            Response.Headers.ContentDisposition = "attachment";
        }
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await source.CopyToAsync(Response.Body, ct);
        return new EmptyResult();
    }

    private static bool IsCardContentType(string? contentType) =>
        contentType?.ToLowerInvariant() is "image/jpeg" or "image/png" or "image/webp";
}
