using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Application.Features.Shop.GetAllModerationShops;
using CoffeePeek.Moderation.Application.Features.Shop.GetModerationShopById;
using CoffeePeek.Moderation.Application.Features.Shop.Menu;
using CoffeePeek.Moderation.Application.Features.Shop.UpdateModerationShopStatus;
using CoffeePeek.Moderation.Application.Features.Shop.UpdateShop;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ModerationShopsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = RoleConsts.Moderator)]
    [Description("Get coffee shops for moderation with pagination and filters")]
    [ProducesResponseType<Response<GetAllModerationShopsResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllModerationShops([FromQuery] GetAllModerationShopsQuery request, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<GetAllModerationShopsResponse>>(request, ct);

        if (response.IsSuccess && response.Data is not null)
        {
            Response.Headers.TryAdd("X-Total-Count", response.Data.TotalItems.ToString());
            Response.Headers.TryAdd("X-Total-Pages", response.Data.TotalPages.ToString());
            Response.Headers.TryAdd("X-Current-Page", response.Data.CurrentPage.ToString());
            Response.Headers.TryAdd("X-Page-Size", response.Data.PageSize.ToString());
        }

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response<ModerationShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetModerationShopById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ModerationShopDto>>(new GetModerationShopByIdQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPost]
    [AllowAnonymous]
    [Description("Adds a new coffee shop to moderation")]
    [ProducesResponseType<Response<SendCoffeeShopToModerationResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendCoffeeShopToModeration(
        [FromBody] SendCoffeeShopToModerationCommand command, CancellationToken ct)
    {
        // Anonymous submissions carry a null UserId; approval consumer skips user crediting for these.
        var commandWithUser = command with { UserId = userContext.GetUserId() };

        var response = await bus.InvokeAsync<Response<SendCoffeeShopToModerationResponse>>(commandWithUser, ct);

        if (!response.IsSuccess)
            return BadRequest(response);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPut]
    [Authorize(Policy = RoleConsts.Moderator)]
    public async Task<IActionResult> UpdateModerationCoffeeShop(
        [FromBody] ModerationShopDto dto, CancellationToken ct)
    {
        var userId = userContext.GetUserIdOrThrow();
        var isPrivilegedModerator = userContext.HasAnyRole(RoleConsts.Moderator, RoleConsts.Admin);
        var command = new UpdateModerationCoffeeShopCommand(dto, userId, isPrivilegedModerator);

        var response = await bus.InvokeAsync<UpdateEntityResponse<ModerationShopDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPut("status")]
    [Authorize(Policy = RoleConsts.Moderator)]
    public async Task<IActionResult> UpdateModerationCoffeeShopStatus(
        [FromQuery, Required] Guid id,
        [FromQuery, Required] ModerationStatus status,
        [FromQuery] string? comment,
        CancellationToken ct)
    {
        var request = new UpdateModerationCoffeeShopStatusCommand(
            userContext.GetUserIdOrThrow(), id, status, comment);

        var response = await bus.InvokeAsync<Response>(request, ct);
        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpPost("{id:guid}/menu/photos")]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response<ModerationShopDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AttachMenuPhotos(
        Guid id,
        [FromBody] AttachMenuPhotosRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ModerationShopDto>>(
            new AttachModerationShopMenuPhotosCommand(id, request.Photos, userContext.GetUserIdOrThrow()), ct);
        return MenuActionResult(response);
    }

    [HttpPost("{id:guid}/menu/parse")]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response<ModerationShopDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ParseMenu(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ModerationShopDto>>(
            new ParseModerationShopMenuCommand(id, userContext.GetUserIdOrThrow()), ct);
        return MenuActionResult(response);
    }

    [HttpPut("{id:guid}/menu")]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response<ModerationShopDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMenu(
        Guid id,
        [FromBody] UpdateShopMenuRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ModerationShopDto>>(
            new UpdateModerationShopMenuCommand(
                id, request.Items, request.ApplySuggestedPriceRange, userContext.GetUserIdOrThrow()),
            ct);
        return MenuActionResult(response);
    }

    private IActionResult MenuActionResult(Response<ModerationShopDto> response)
    {
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            StatusCodes.Status400BadRequest => BadRequest(response),
            _ => StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response)
        };
    }
}
