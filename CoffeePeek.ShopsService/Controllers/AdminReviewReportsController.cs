using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Inspect complaints and explicitly dismiss them or remove the reported review.</summary>
[ApiController]
[Route("api/admin/review-reports")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminReviewReportsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>List complaints, pending by default, with bounded pagination.</summary>
    [HttpGet]
    [ProducesResponseType<Response<ReviewReportPage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct,
        [FromQuery] ReviewReportStatus? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        Ok(await bus.InvokeAsync<Response<ReviewReportPage>>(new GetReviewReportsQuery(status ?? ReviewReportStatus.Pending, page, pageSize), ct));

    /// <summary>Read complaint details and the current reported review.</summary>
    [HttpGet("{reportId:guid}")]
    [ProducesResponseType<Response<ReviewReportDetail>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid reportId, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<ReviewReportDetail>>(new GetReviewReportQuery(reportId), ct));

    /// <summary>Dismiss a complaint or permanently hide its review.</summary>
    [HttpPut("{reportId:guid}/resolution")]
    [ProducesResponseType<Response<ReviewReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(Guid reportId, [FromBody] ResolveReviewReportRequest request, CancellationToken ct)
    {
        if (!request.DeleteReview.HasValue) return BadRequest("deleteReview is required.");
        return Ok(await bus.InvokeAsync<Response<ReviewReportDto>>(
            new ResolveReviewReportCommand(reportId, userContext.GetUserIdOrThrow(), request.DeleteReview.Value), ct));
    }
}
