using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Moderation.Application.Features.Admin.Stats;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

/// <summary>Internal admin statistics for the Moderation service.</summary>
[ApiController]
[Route("api/admin/stats")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminStatsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Returns pending moderation shop and review counts for the admin dashboard.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<Response<AdminServiceStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AdminServiceStatsDto>>(
            new GetAdminModerationStatsQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns review moderation SLA (30 days), per-queue pending counts with oldest pending age,
    /// and top moderator workload (30 days).
    /// </summary>
    [HttpGet("moderation/insights")]
    [ProducesResponseType<Response<AdminModerationInsightsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInsights(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AdminModerationInsightsDto>>(
            new GetAdminModerationInsightsQuery(), cancellationToken);
        return Ok(response);
    }
}
