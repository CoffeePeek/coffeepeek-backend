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
/// Address metadata comes directly from the read-only <see cref="IProfileAddressService"/>, as in the phase 2 and 3
/// public address controllers: there is no new Wolverine handler, so the pre-generated handler code does not change.
/// Profile data still goes through <see cref="IMessageBus"/> with the existing <see cref="GetPublicUserProfileCommand"/>,
/// which keeps a single mapping path and re-checks visibility on the data read (D-04).
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
    [ProducesResponseType<PublicAddressEnvelope<PublicUserProfileResponse>>(StatusCodes.Status200OK)]
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
                ? Ok(new PublicAddressEnvelope<PublicUserProfileResponse>(response.Data, address))
                : NotFound();
        }
        catch (NotFoundException)
        {
            // The user was deleted or blocked between the resolve and the read. Answer exactly like an unknown slug,
            // never with the descriptive ErrorResponse the global exception handler would produce.
            return NotFound();
        }
    }

    /// <summary>
    /// Returns the canonical addresses of up to 100 visible users, in input order; hidden and unknown ids are omitted.
    /// </summary>
    [HttpGet("public-addresses")]
    [ProducesResponseType<PublicAddressMetadata[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicAddressMetadata[]>> GetBatch([FromQuery] Guid[] ids, CancellationToken ct) =>
        Ok(await addresses.GetByIdsAsync(ids, ct));

    /// <summary>
    /// Returns the current canonical address for a legacy user GUID, or 404 when the user is unknown or hidden.
    /// </summary>
    [HttpGet("{id:guid}/public-address")]
    [ProducesResponseType<PublicAddressMetadata>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await addresses.GetByIdsAsync([id], ct);
        return result.Length == 0 ? NotFound() : Ok(result[0]);
    }
}
