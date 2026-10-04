using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.AccountService.Controllers;

/// <summary>Public-only author details for administrative UUID references.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class AdminUserProfilesController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{id:guid}/profile")]
    [ProducesResponseType<Response<PublicUserProfileResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(Guid id, CancellationToken ct)
    {
        try
        {
            var response = await bus.InvokeAsync<Response<PublicUserProfileResponse>>(new GetPublicUserProfileCommand(id), ct);
            return response is { IsSuccess: true, Data: not null } ? Ok(response) : NotFound();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }
}
