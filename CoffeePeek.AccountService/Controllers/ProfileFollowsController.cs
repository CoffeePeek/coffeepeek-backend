using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Contract.Dtos.Profiles;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[Route("api/Users/{slug}")]
[Tags("User")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileFollowsController(
    IProfileAddressService addresses,
    IProfileSocialStore social,
    IUserContext userContext) : ControllerBase
{
    [HttpPut("follow")]
    [Authorize]
    [ProducesResponseType<ProfileFollowStateDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Follow(string slug, CancellationToken ct) => SetAsync(slug, true, ct);

    [HttpDelete("follow")]
    [Authorize]
    [ProducesResponseType<ProfileFollowStateDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Unfollow(string slug, CancellationToken ct) => SetAsync(slug, false, ct);

    [HttpGet("followers")]
    [ProducesResponseType<ProfileFollowPageDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Followers(string slug, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        ListAsync(slug, false, page, pageSize, ct);

    [HttpGet("following")]
    [ProducesResponseType<ProfileFollowPageDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Following(string slug, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        ListAsync(slug, true, page, pageSize, ct);

    private async Task<IActionResult> SetAsync(string slug, bool following, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(slug, ct);
        if (address is null) return NotFound();
        try
        {
            var viewer = userContext.GetUserIdOrThrow();
            await social.SetAsync(viewer, address.EntityId, following, ct);
            return Ok(await social.GetStateAsync(address.EntityId, viewer, ct));
        }
        catch (NotFoundException) { return NotFound(); }
    }

    private async Task<IActionResult> ListAsync(string slug, bool following, int page, int pageSize, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(slug, ct);
        if (address is null) return NotFound();
        try { return Ok(await social.ListAsync(address.EntityId, following, page, pageSize, ct, userContext.GetUserId())); }
        catch (NotFoundException) { return NotFound(); }
    }
}
