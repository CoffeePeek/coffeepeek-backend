using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

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
public sealed class PublicUserAddressesController(IProfileAddressService addresses, IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Resolves a profile slug or alias (case-insensitive) to the public profile and its current canonical address.
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

        try
        {
            var response = await bus.InvokeAsync<Response<PublicUserProfileResponse>>(
                new GetPublicUserProfileCommand(address.EntityId), ct);
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
