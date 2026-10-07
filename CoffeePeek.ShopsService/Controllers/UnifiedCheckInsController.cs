using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Route("api/v1/check-ins")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UnifiedCheckInsController(IMessageBus bus, IUserContext userContext, ICheckInQueries queries, PublicDiscoveryService discovery, IProfileAddressLookup profiles) : ControllerBase
{
    [Authorize]
    [HttpPost]
    [ProducesResponseType(typeof(Response<PublicCheckInDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(PublicCreateUnifiedCheckInRequest request, CancellationToken ct)
    {
        var shopId = await discovery.ResolveIdAsync(PublicAddressKind.CoffeeShop, request.CoffeeShopSlug, ct);
        var internalRequest = new CreateUnifiedCheckInRequest(shopId, request.Text, request.Rating,
            request.Visibility, request.DrinkSlug, request.CustomDrinkName, request.Photos, request.VisitedAt);
        return await ResultAsync(await bus.InvokeAsync<Response<UnifiedCheckInDto>>(new CreateUnifiedCheckInCommand(
            internalRequest, userContext.GetUserIdOrThrow(), userContext.GetUsernameOrThrow()), ct), ct);
    }

    [Authorize]
    [HttpGet("mine")]
    [ProducesResponseType(typeof(Response<PublicCanonicalOwnerCheckInPage>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from >= to) return BadRequest(new ErrorResponse("'from' must be earlier than 'to'."));
        pageNumber = Math.Clamp(pageNumber, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await queries.GetUnifiedByUserAsync(userContext.GetUserIdOrThrow(), pageNumber, pageSize,
            from?.UtcDateTime, to?.UtcDateTime, ct);
        Response.Headers["X-Total-Count"] = result.TotalCount.ToString();
        Response.Headers["X-Total-Pages"] = ((int)Math.Ceiling(result.TotalCount / (double)pageSize)).ToString();
        Response.Headers["X-Current-Page"] = pageNumber.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        return Ok(Response<PublicCanonicalOwnerCheckInPage>.Success(new(await discovery.CheckInsAsync(result.Items, ct), result.TotalCount)));
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<PublicCheckInDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await queries.GetUnifiedByIdAsync(id, userContext.GetUserId(), ct);
        return result == null ? NotFound(new ErrorResponse("Check-in not found.")) : await ResultAsync(Response<UnifiedCheckInDto>.Success(result), ct);
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(Response<PublicCanonicalCheckInPage>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Feed([FromQuery] string? citySlug = null, [FromQuery] string? coffeeShopSlug = null,
        [FromQuery] string? authorSlug = null, [FromQuery] int pageSize = 20, [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var cityId = citySlug == null ? (Guid?)null : await discovery.ResolveIdAsync(PublicAddressKind.City,citySlug,ct);
        var shopId = coffeeShopSlug == null ? (Guid?)null : await discovery.ResolveIdAsync(PublicAddressKind.CoffeeShop,coffeeShopSlug,ct);
        var authorId = authorSlug == null ? (Guid?)null : (await profiles.ResolveAsync(authorSlug,ct))?.EntityId;
        if(authorSlug != null && authorId == null) return NotFound();
        var page = await queries.GetPublicAsync(userContext.GetUserId(),cityId,shopId,authorId,pageSize,cursor,ct);
        return Ok(Response<PublicCanonicalCheckInPage>.Success(new(await discovery.CheckInsAsync(page.Items,ct),page.NextCursor)));
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateUnifiedCheckInRequest request, CancellationToken ct) =>
        await ResultAsync(await bus.InvokeAsync<Response<UnifiedCheckInDto>>(new UpdateUnifiedCheckInCommand(id,
            userContext.GetUserIdOrThrow(), request), ct), ct);

    [Authorize]
    [HttpPut("{id:guid}/visibility")]
    public async Task<IActionResult> Visibility(Guid id, ChangeCheckInVisibilityRequest request, CancellationToken ct) =>
        await ResultAsync(await bus.InvokeAsync<Response<UnifiedCheckInDto>>(new ChangeUnifiedCheckInVisibilityCommand(id,
            userContext.GetUserIdOrThrow(), request.Visibility), ct), ct);

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Result(await bus.InvokeAsync<Response>(new DeleteUnifiedCheckInCommand(id, userContext.GetUserIdOrThrow()), ct));

    [Authorize]
    [HttpPut("{id:guid}/helpful")]
    public async Task<IActionResult> Helpful(Guid id, CancellationToken ct) =>
        Result(await bus.InvokeAsync<Response<CheckInHelpfulResponse>>(new SetCheckInHelpfulCommand(id, userContext.GetUserIdOrThrow(), true), ct));

    [Authorize]
    [HttpDelete("{id:guid}/helpful")]
    public async Task<IActionResult> Unhelpful(Guid id, CancellationToken ct) =>
        Result(await bus.InvokeAsync<Response<CheckInHelpfulResponse>>(new SetCheckInHelpfulCommand(id, userContext.GetUserIdOrThrow(), false), ct));

    private async Task<IActionResult> ResultAsync(Response<UnifiedCheckInDto> response, CancellationToken ct)
    {
        if (!response.IsSuccess) return StatusCode(response.StatusCode ?? 400,response);
        return Ok(Response<PublicCheckInDto>.Success((await discovery.CheckInsAsync([response.Data],ct)).Single(),response.Message));
    }
    private IActionResult Result(Response result) => result.IsSuccess ? Ok(result) : StatusCode(result.StatusCode ?? 400, result);
}
