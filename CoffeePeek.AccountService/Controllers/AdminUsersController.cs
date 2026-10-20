using CoffeePeek.Account.Application.Features.Admin.Users;
using CoffeePeek.Account.Application.Features.Admin.Users.Sessions;
using CoffeePeek.AccountService.Controllers.Admin;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.AccountService.Controllers;

/// <summary>Admin user management endpoints.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminUsersController(IMessageBus bus, IUserContext userContext) : ControllerBase
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

    /// <summary>Lists refresh-token sessions for a user (token value omitted).</summary>
    [HttpGet("{userId:guid}/sessions")]
    [ProducesResponseType<Response<GetAdminUserSessionsResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessions(Guid userId, CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<GetAdminUserSessionsResponse>>(
            new GetAdminUserSessionsQuery(userId), cancellationToken);

        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    /// <summary>Revokes a single refresh-token session.</summary>
    [HttpDelete("{userId:guid}/sessions/{sessionId:guid}")]
    [ProducesResponseType<Response<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<bool>>(
            new RevokeAdminUserSessionCommand(userId, sessionId), cancellationToken);

        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    /// <summary>Revokes all refresh-token sessions for a user.</summary>
    [HttpDelete("{userId:guid}/sessions")]
    [ProducesResponseType<Response<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAllSessions(Guid userId, CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<bool>>(
            new RevokeAllAdminUserSessionsCommand(userId), cancellationToken);

        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    /// <summary>Replaces the target user's role.</summary>
    [HttpPatch("{id:guid}/role")]
    [ProducesResponseType<Response<Guid>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateRole(
        Guid id,
        [FromBody] UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateUserRoleCommand(id, request.Role, userContext.GetUserIdOrThrow());
        return Result(await bus.InvokeAsync<Response<Guid>>(command, cancellationToken));
    }

    /// <summary>Blocks or unblocks a user. Blocking revokes all active sessions.</summary>
    [HttpPatch("{id:guid}/block")]
    [ProducesResponseType<Response<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BlockUser(
        Guid id,
        [FromBody] BlockUserRequest request,
        CancellationToken cancellationToken)
    {
        return Result(await bus.InvokeAsync<Response<bool>>(
            new BlockUserCommand(id, request.Blocked, userContext.GetUserIdOrThrow()), cancellationToken));
    }

    /// <summary>Soft-deletes a user and revokes all active sessions.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<Response<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        return Result(await bus.InvokeAsync<Response<bool>>(
            new AdminDeleteUserCommand(id, userContext.GetUserIdOrThrow()), cancellationToken));
    }

    private ObjectResult Result<T>(Response<T> response) =>
        StatusCode(response.IsSuccess ? StatusCodes.Status200OK : response.StatusCode ?? StatusCodes.Status400BadRequest, response);
}
