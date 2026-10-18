using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Shared.Auth;

namespace CoffeePeek.AccountService.Controllers;

/// <summary>
/// Anonymous public profile addresses under the existing <c>api/Users</c> prefix.
/// Profile data uses the existing handler and rechecks visibility after resolving the address.
/// Every hidden case (unknown, retired, deleted or blocked) returns the same 404 with the framework's generic
/// ProblemDetails body and no user data.
/// </summary>
[ApiController]
[Route("api/Users")]
[Tags("User")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class PublicUserAddressesController(IProfileAddressService addresses, IMessageBus bus, IUserBlockStore blocks, IUserContext? userContext = null) : ControllerBase
{
    /// <summary>
    /// Resolves the current profile slug (case-insensitive) to the public profile and its address.
    /// </summary>
    [HttpGet("by-slug/{slug}")]
    [HttpGet("{slug}")]
    [ProducesResponseType<PublicEntityEnvelope<PublicUserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(slug, ct);
        if (address is null) return NotFound();

        if (userContext?.GetUserId() is { } viewer)
        {
            var outgoing = await blocks.FindOutgoingAsync(viewer, address.EntityId, ct);
            if (outgoing is not null)
                return Ok(new PublicEntityEnvelope<PublicUserProfileResponse>(
                    new PublicUserProfileResponse(outgoing.UserName, default, null, null, 0, 0)
                    { AuthorId = outgoing.AuthorId, IsBlockedByViewer = true }, PublicAddressDto.From(address)));
            if ((await blocks.ExcludedUserIdsAsync(viewer, ct)).Contains(address.EntityId)) return NotFound();
        }
        try
        {
            var response = await bus.InvokeAsync<Response<PublicUserProfileResponse>>(
                new GetPublicUserProfileCommand(address.EntityId) { ViewerId = userContext?.GetUserId() }, ct);
            return response is { IsSuccess: true, Data: not null }
                ? Ok(new PublicEntityEnvelope<PublicUserProfileResponse>(response.Data, PublicAddressDto.From(address)))
                : NotFound();
        }
        catch (NotFoundException)
        {
            // The user was deleted or blocked between the resolve and the read. Answer exactly like an unknown slug,
            // never with the descriptive ErrorResponse the global exception handler would produce.
            return NotFound();
        }
    }

}
