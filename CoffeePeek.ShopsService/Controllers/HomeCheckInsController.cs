using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Home preparations with author-controlled visibility.</summary>
[ApiController]
[Authorize]
[Route("api/v1/check-ins/home")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HomeCheckInsController(IMessageBus bus, IUserContext userContext, ICheckInQueries queries) : ControllerBase
{
    /// <summary>Create a home preparation (private by default) for the signed-in user.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<HomeCheckInDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateHomeCheckInRequest request, CancellationToken ct) =>
        Result(await bus.InvokeAsync<Response<HomeCheckInDto>>(
            new CreateHomeCheckInCommand(request, userContext.GetUserIdOrThrow(), userContext.GetUsernameOrThrow()), ct));

    /// <summary>List the owner's brews in preparation-time order. Date bounds are inclusive.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<HomeCheckInPage>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        if (from > to) return BadRequest(new ErrorResponse("'from' must be no later than 'to'."));
        pageNumber = Math.Clamp(pageNumber, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var page = await queries.GetHomeByUserAsync(userContext.GetUserIdOrThrow(), pageNumber, pageSize, from, to, ct);
        Response.Headers["X-Total-Count"] = page.TotalCount.ToString();
        Response.Headers["X-Total-Pages"] = ((int)Math.Ceiling(page.TotalCount / (double)pageSize)).ToString();
        Response.Headers["X-Current-Page"] = pageNumber.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        return Ok(Response<HomeCheckInPage>.Success(page));
    }

    /// <summary>Return brew counts per local preparation date, including backdated entries.</summary>
    [HttpGet("calendar")]
    [ProducesResponseType(typeof(Response<HomeCalendarDayDto[]>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Calendar([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (from == null || to == null || from > to || to.Value.DayNumber - from.Value.DayNumber > 366)
            return BadRequest(new ErrorResponse("Supply 'from' and 'to' calendar dates in order, at most 366 days apart."));
        var days = await queries.GetHomeCalendarAsync(userContext.GetUserIdOrThrow(), from.Value, to.Value, ct);
        return Ok(Response<HomeCalendarDayDto[]>.Success(days));
    }

    /// <summary>Return the signed-in user's bean statistics.</summary>
    [HttpGet("stats/beans")]
    [ProducesResponseType(typeof(Response<HomeBeanStatsDto[]>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BeanStats(CancellationToken ct) =>
        Ok(Response<HomeBeanStatsDto[]>.Success(await queries.GetHomeBeanStatsAsync(userContext.GetUserIdOrThrow(), ct)));

    /// <summary>Return the signed-in user's brew-method statistics.</summary>
    [HttpGet("stats/methods")]
    [ProducesResponseType(typeof(Response<HomeMethodStatsDto[]>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MethodStats(CancellationToken ct) =>
        Ok(Response<HomeMethodStatsDto[]>.Success(await queries.GetHomeMethodStatsAsync(userContext.GetUserIdOrThrow(), ct)));

    /// <summary>Read an owned home preparation or an approved public preparation.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Response<HomeCheckInDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entry = await queries.GetHomeByIdAsync(id, userContext.GetUserId(), ct);
        return entry == null ? NotFound(new ErrorResponse("Home check-in not found."))
            : Ok(Response<HomeCheckInDto>.Success(entry));
    }

    /// <summary>Replace editable fields; existing photos are retained.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<HomeCheckInDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHomeCheckInRequest request, CancellationToken ct) =>
        Result(await bus.InvokeAsync<Response<HomeCheckInDto>>(
            new UpdateHomeCheckInCommand(id, userContext.GetUserIdOrThrow(), request), ct));

    /// <summary>Soft-delete one home preparation owned by the signed-in user.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response>(new DeleteHomeCheckInCommand(id, userContext.GetUserIdOrThrow()), ct);
        return response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? 400, response);
    }

    private IActionResult Result(Response<HomeCheckInDto> response) =>
        response.IsSuccess ? Ok(response) : StatusCode(response.StatusCode ?? 400, response);
}
