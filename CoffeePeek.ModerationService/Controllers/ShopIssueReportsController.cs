using CoffeePeek.Moderation.Application.Features.ShopIssueReports.CreateShopIssueReport;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Moderation.Application.Services;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ShopIssueReportsController(IMessageBus bus, IUserContext userContext, PublicModerationService discovery) : ControllerBase
{
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(CreateEntityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateShopIssueReport(
        [FromBody] PublicCreateShopIssueRequest request, CancellationToken ct)
    {
        var command = new CreateShopIssueReportCommand(await WriteStageTrace.MeasureAsync("shop_report", "shop_lookup",
            () => discovery.ResolveIdAsync("shops", request.Shop, ct)), request.Category, request.Description)
        { UserId = userContext.GetUserIdOrThrow() };

        var response = await WriteStageTrace.MeasureAsync("shop_report", "commit",
            () => bus.InvokeAsync<CreateEntityResponse>(command, ct));

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
