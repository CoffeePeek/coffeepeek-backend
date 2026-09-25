using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;
using CoffeePeek.Shops.Application.Features.CheckIn.GetUserCheckIns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[Tags("Check-ins")]
[Produces("application/json")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class CheckInsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    /// <summary>
    /// Create a new check-in at a coffee shop
    /// </summary>
    /// <remarks>
    /// **Process:**
    /// 1. Takes shop ID and optional message from body.
    /// 2. Automatically injects User ID and Name from Gateway headers.
    /// 3. Registers the visit in the system.
    /// </remarks>
    /// <param name="command">Check-in details (ShopId is required)</param>
    /// <response code="200">Check-in successfully created</response>
    /// <response code="400">Invalid shop ID or business logic violation</response>
    /// <response code="401">Unauthorized: Missing user context headers</response>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CreateCheckInResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateCheckIn([FromBody] CreateCheckInCommand command)
    {
        command = command with { UserId = userContext.GetUserIdOrThrow(), UserName = userContext.GetUsernameOrThrow() };
        var response = await bus.InvokeAsync<Response<CreateCheckInResponse>>(command);

        return Ok(response);
    }

    /// <summary>
    /// Get paginated list of check-ins for the current user
    /// </summary>
    /// <remarks>
    /// **Note on Pagination:**
    /// This endpoint uses **Custom Headers** for pagination control instead of query parameters.
    /// 
    /// **Response Headers:**
    /// - `X-Total-Count`: Total number of check-ins
    /// - `X-Total-Pages`: Total number of pages
    /// - `X-Current-Page`: Current page index
    ///
    /// **Date range (calendar view):** optional `from` / `to` query parameters filter by visit time,
    /// half-open `[from, to)`. Send ISO 8601 with an offset, e.g. `?from=2026-09-01T00:00:00Z&amp;to=2026-10-01T00:00:00Z`.
    /// </remarks>
    /// <param name="pageNumber">Page index (starts from 1)</param>
    /// <param name="pageSize">Items per page (max 100)</param>
    /// <param name="from">Inclusive lower bound on visit time</param>
    /// <param name="to">Exclusive upper bound on visit time</param>
    /// <response code="200">Returns list of user's check-ins</response>
    /// <response code="400">`from` is not earlier than `to`</response>
    /// <response code="401">Unauthorized: Missing user context headers</response>
    [HttpGet]
    [ProducesResponseType(typeof(Response<GetUserCheckInsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyCheckIns(
        [FromHeader(Name = "X-Page-Number")] int pageNumber = 1,
        [FromHeader(Name = "X-Page-Size")] int pageSize = 10,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        if (from.HasValue && to.HasValue && from >= to)
            return BadRequest(new ErrorResponse("'from' must be earlier than 'to'."));

        pageNumber = Math.Clamp(pageNumber, 1, 10_000); // larger values overflow Skip((page-1)*size)
        pageSize = pageSize <= 0 ? 10 : Math.Min(pageSize, 100);

        var userId = userContext.GetUserIdOrThrow();
        
        var command = new GetUserCheckInsCommand(userId, pageNumber, pageSize, from?.UtcDateTime, to?.UtcDateTime);
        var response = await bus.InvokeAsync<Response<GetUserCheckInsResponse>>(command);

        if (response is { IsSuccess: true, Data: not null })
        {
            AddPaginationHeaders(response.Data.TotalItems, response.Data.TotalPages, response.Data.CurrentPage, response.Data.PageSize);
        }

        return Ok(response);
    }

    private void AddPaginationHeaders(int totalItems, int totalPages, int currentPage, int pageSize)
    {
        Response.Headers.TryAdd("X-Total-Count", totalItems.ToString());
        Response.Headers.TryAdd("X-Total-Pages", totalPages.ToString());
        Response.Headers.TryAdd("X-Current-Page", currentPage.ToString());
        Response.Headers.TryAdd("X-Page-Size", pageSize.ToString());
    }
}