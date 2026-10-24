using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shops.Application.Features.Admin.Stats;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Internal admin statistics for the Shops service.</summary>
[ApiController]
[Route("api/admin/stats")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminStatsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Returns coffee shop and review counts for the admin dashboard.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<Response<AdminServiceStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AdminServiceStatsDto>>(
            new GetAdminShopsStatsQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Returns rating distribution/averages, top shops by check-ins (30 days) and reviews,
    /// and app download redirect statistics.
    /// </summary>
    [HttpGet("shops/insights")]
    [ProducesResponseType<Response<AdminShopsInsightsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInsights(CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AdminShopsInsightsDto>>(
            new GetAdminShopsInsightsQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>Returns zero-filled per-UTC-day counts of new shops, reviews and check-ins ending today.</summary>
    /// <param name="days">Window length in days; default 30, clamped to 1..90.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("shops/timeseries")]
    [ProducesResponseType<Response<AdminShopsTimeseriesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeseries([FromQuery] int? days, CancellationToken cancellationToken)
    {
        var response = await bus.InvokeAsync<Response<AdminShopsTimeseriesDto>>(
            new GetAdminShopsTimeseriesQuery(days), cancellationToken);
        return Ok(response);
    }
}
