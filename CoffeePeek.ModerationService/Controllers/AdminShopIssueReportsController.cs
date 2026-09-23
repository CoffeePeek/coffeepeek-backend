using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.ShopIssueReports.ChangeShopIssueReportStatus;
using CoffeePeek.Moderation.Application.Features.ShopIssueReports.GetShopIssueReports;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[Route("api/admin/shop-reports")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin Shop Reports")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminShopIssueReportsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<GetShopIssueReportsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShopIssueReports(
        [FromQuery] GetShopIssueReportsQuery query, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetShopIssueReportsResponse>>(query, ct);

        if (response.IsSuccess && response.Data is not null)
        {
            Response.Headers.TryAdd("X-Total-Count", response.Data.TotalItems.ToString());
            Response.Headers.TryAdd("X-Total-Pages", response.Data.TotalPages.ToString());
            Response.Headers.TryAdd("X-Current-Page", response.Data.CurrentPage.ToString());
            Response.Headers.TryAdd("X-Page-Size", response.Data.PageSize.ToString());
        }

        return Ok(response);
    }

    [HttpPut("{reportId:guid}/status")]
    [ProducesResponseType<UpdateEntityResponse<ShopIssueReportStatus>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeShopIssueReportStatus(
        Guid reportId, [FromBody] ChangeShopIssueReportStatusCommand command, CancellationToken ct)
    {
        var commandWithUser = command with { ReportId = reportId, UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<UpdateEntityResponse<ShopIssueReportStatus>>(commandWithUser, ct);

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
