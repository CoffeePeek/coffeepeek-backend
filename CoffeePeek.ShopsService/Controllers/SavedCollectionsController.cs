using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController, Authorize, Route("api/v1/saved-collections")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SavedCollectionsController(ISavedCollectionsStore collections, IUserContext user, IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Mine([FromQuery] string? kind, [FromQuery] string? slug, CancellationToken ct) =>
        Ok(Response<SavedCollectionSummary[]>.Success(await collections.ListAsync(user.GetUserIdOrThrow(), kind, slug, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await collections.GetAsync(user.GetUserIdOrThrow(), id, ct);
        return row == null ? NotFound() : Ok(Response<SavedCollectionDto>.Success(row));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSavedCollectionRequest request, CancellationToken ct)
    {
        var row = await bus.InvokeAsync<SavedCollectionDto>(new CreateSavedCollectionCommand(user.GetUserIdOrThrow(), request), ct);
        return CreatedAtAction(nameof(Get), new { id = row.Id }, Response<SavedCollectionDto>.Success(row));
    }

    [HttpPut("default")]
    public async Task<IActionResult> EnsureDefault(CancellationToken ct)
    {
        var row = await bus.InvokeAsync<SavedCollectionDto>(new CreateSavedCollectionCommand(
            user.GetUserIdOrThrow(), new CreateSavedCollectionRequest { Title = "Избранное", IsDefault = true }), ct);
        return Ok(Response<SavedCollectionDto>.Success(row));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSavedCollectionRequest request, CancellationToken ct)
    {
        var row = await bus.InvokeAsync<SavedCollectionDto?>(new UpdateSavedCollectionCommand(user.GetUserIdOrThrow(), id, request), ct);
        return row == null ? NotFound() : Ok(Response<SavedCollectionDto>.Success(row));
    }

    [HttpPut("{id:guid}/items")]
    public async Task<IActionResult> ReplaceItems(Guid id, [FromBody] ReplaceSavedCollectionItemsRequest request, CancellationToken ct)
    {
        var row = await bus.InvokeAsync<SavedCollectionDto?>(new ReplaceSavedCollectionItemsCommand(user.GetUserIdOrThrow(), id, request), ct);
        return row == null ? NotFound() : Ok(Response<SavedCollectionDto>.Success(row));
    }

    [HttpPut("membership/{kind}/{slug}")]
    public async Task<IActionResult> SetMembership(string kind, string slug, [FromBody] SetSavedCollectionMembershipRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<SavedCollectionMembershipResult>(
            new SetSavedCollectionMembershipCommand(user.GetUserIdOrThrow(), kind, slug, request.CollectionIds,
                request.AddedCollectionIds, request.RemovedCollectionIds), ct);
        Response.Headers["X-CoffeePeek-Is-Favorite"] = result.IsFavorite ? "true" : "false";
        Response.Headers["X-CoffeePeek-Membership-Changed"] = result.MembershipChanged ? "true" : "false";
        return Ok(Response<SavedCollectionSummary[]>.Success(result.Collections));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await bus.InvokeAsync<bool>(new DeleteSavedCollectionCommand(user.GetUserIdOrThrow(), id), ct) ? NoContent() : NotFound();

    [HttpGet("featured")]
    public async Task<IActionResult> Featured(CancellationToken ct) =>
        Ok(Response<FeaturedCollectionPreference>.Success(await collections.FeaturedAsync(user.GetUserIdOrThrow(), ct)));

    [HttpPut("featured")]
    public async Task<IActionResult> SetFeatured([FromBody] FeaturedCollectionPreference request, CancellationToken ct) =>
        Ok(Response<FeaturedCollectionPreference>.Success(await bus.InvokeAsync<FeaturedCollectionPreference>(
            new SetFeaturedCollectionCommand(user.GetUserIdOrThrow(), request.CollectionId), ct)));
}
