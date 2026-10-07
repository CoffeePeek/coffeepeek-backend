using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/check-ins/{id:guid}/reports")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CheckInReportsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>Submit complaint text about a currently approved public check-in.</summary>
    [HttpPost]
    [ProducesResponseType<Response<ReviewReportDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Report(Guid id, [FromBody] SubmitReviewReportRequest request, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ReviewReportDto>>(
            new SubmitReviewReportCommand(id, userContext.GetUserIdOrThrow(), request.Text), ct);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
