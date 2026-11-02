using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController, Authorize, Route("api/v1/favorite-collections")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FavoriteCollectionsController(IFavoriteCollectionsStore collections, IUserContext user, IMessageBus bus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFavoriteCollectionRequest request, CancellationToken ct)
    {
        var collection = await bus.InvokeAsync<FavoriteCollectionDto>(new CreateFavoriteCollectionCommand(user.GetUserIdOrThrow(), request), ct);
        return CreatedAtAction(nameof(Get), new { id = collection.Id }, Response<FavoriteCollectionDto>.Success(collection));
    }

    [AllowAnonymous, HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var collection = await collections.GetAsync(id, ct);
        return collection is null ? NotFound() : Ok(Response<FavoriteCollectionDto>.Success(collection));
    }

    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        Ok(Response<FavoriteCollectionSummary[]>.Success(await collections.ListAsync(user.GetUserIdOrThrow(), ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        await bus.InvokeAsync<bool>(new RevokeFavoriteCollectionCommand(user.GetUserIdOrThrow(), id), ct) ? NoContent() : NotFound();
}
