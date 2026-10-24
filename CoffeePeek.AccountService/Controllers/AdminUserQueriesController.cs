using CoffeePeek.Account.Application.Features.Admin.Users;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.AccountService.Controllers;

/// <summary>Read-only user list and dashboard statistics for moderators and administrators.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminUserQueriesController(IMessageBus bus) : ControllerBase
{
    /// <summary>Returns a paginated list of users with optional search and role filter.</summary>
    [HttpGet]
    [ProducesResponseType<Response<GetAdminUsersResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] GetAdminUsersQuery query,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<GetAdminUsersResponse>>(query, cancellationToken);

        if (response.IsSuccess && response.Data is not null)
        {
            Response.Headers.TryAdd("X-Total-Count", response.Data.TotalItems.ToString());
            Response.Headers.TryAdd("X-Total-Pages", response.Data.TotalPages.ToString());
            Response.Headers.TryAdd("X-Current-Page", response.Data.CurrentPage.ToString());
            Response.Headers.TryAdd("X-Page-Size", response.Data.PageSize.ToString());
        }

        return Ok(response);
    }

    /// <summary>Returns aggregate user statistics for the admin dashboard.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<Response<GetAdminUsersStatsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<GetAdminUsersStatsResponse>>(
            new GetAdminUsersStatsQuery(), cancellationToken);
        return Ok(response);
    }
}
