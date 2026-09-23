using CoffeePeek.Moderation.Application.Features.ShopIssueReports.CreateShopIssueReport;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ShopIssueReportsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(CreateEntityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateShopIssueReport(
        [FromBody] CreateShopIssueReportCommand command, CancellationToken ct)
    {
        command = command with { UserId = userContext.GetUserIdOrThrow() };

        var response = await bus.InvokeAsync<CreateEntityResponse>(command, ct);

        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }
}
