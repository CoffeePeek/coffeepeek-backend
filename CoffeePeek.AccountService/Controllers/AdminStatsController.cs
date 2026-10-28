using CoffeePeek.Account.Application.Features.Admin.Stats;
using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.AccountService.Controllers;

/// <summary>Admin dashboard statistics endpoints.</summary>
[ApiController]
[Route("api/admin/stats")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminStatsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Returns platform-wide overview statistics. Always 200; missing services report 0.</summary>
    [HttpGet("overview")]
    [ProducesResponseType<Response<AdminOverviewStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AdminOverviewStatsDto>>(
            new GetAdminOverviewStatsQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns registrations and daily active users per UTC calendar day, zero-filled, ending today (incomplete).
    /// </summary>
    /// <param name="days">Window length in days; default 30, clamped to 1..90.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("users/timeseries")]
    [ProducesResponseType<Response<AdminUsersTimeseriesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsersTimeseries([FromQuery] int? days, CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AdminUsersTimeseriesDto>>(
            new GetAdminUsersTimeseriesQuery(days), cancellationToken);
        return Ok(response);
    }
}
