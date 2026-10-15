using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DiscoveryFavoritesController(IDiscoveryFavoritesStore favorites, IUserContext user, IMessageBus bus,
    IProfileAddressLookup profiles) : ControllerBase
{
    // Favorites form the public "Любимые" section of a profile. Account filters unavailable users.
    [AllowAnonymous]
    [HttpGet("api/v1/profiles/{authorSlug}/favorites")]
    [ProducesResponseType(typeof(Response<DiscoveryFavoriteItem[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PublicList(string authorSlug, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(authorSlug, ct);
        if (profile is null) return NotFound();
        return Ok(Response<DiscoveryFavoriteItem[]>.Success(await favorites.ListAsync(profile.EntityId, ct)));
    }

    [HttpGet("api/v1/favorites")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(Response<DiscoveryFavoriteItem[]>.Success(await favorites.ListAsync(user.GetUserIdOrThrow(), ct)));
    [HttpPut("api/v1/favorites/{kind}/{slug}")]
    public async Task<IActionResult> Add(string kind, string slug, CancellationToken ct)
    { await bus.InvokeAsync(new SetDiscoveryFavoriteCommand(user.GetUserIdOrThrow(), kind, slug, true), ct); return NoContent(); }
    [HttpDelete("api/v1/favorites/{kind}/{slug}")]
    public async Task<IActionResult> Remove(string kind, string slug, CancellationToken ct)
    { await bus.InvokeAsync(new SetDiscoveryFavoriteCommand(user.GetUserIdOrThrow(), kind, slug, false), ct); return NoContent(); }
}
