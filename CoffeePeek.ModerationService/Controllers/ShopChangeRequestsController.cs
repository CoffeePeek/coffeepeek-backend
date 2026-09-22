using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ModerationService.Controllers;

public record UpdateShopChangeRequestBody(ShopChangeSection Section, ShopChangePayloadDto Payload);
public record DecideShopChangeRequestBody(ModerationStatus Status, string? Comment);

[ApiController]
[Route("api/ShopChangeRequests")]
[Authorize]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ShopChangeRequestsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<ShopChangeRequestDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateShopChangeRequestCommand command,
        CancellationToken ct)
    {
        command = command with { UserId = userContext.GetUserIdOrThrow() };
        var response = await bus.InvokeAsync<Response<ShopChangeRequestDto>>(command, ct);
        return response.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpGet("mine")]
    [ProducesResponseType<Response<ShopChangeRequestPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ModerationStatus? status = null,
        [FromQuery] Guid? shopId = null,
        [FromQuery] ShopChangeSection? section = null,
        CancellationToken ct = default)
    {
        var response = await bus.InvokeAsync<Response<ShopChangeRequestPageDto>>(
            new GetMyShopChangeRequestsQuery(
                userContext.GetUserIdOrThrow(), page, pageSize, status, shopId, section), ct);
        AddPagingHeaders(response.Data);
        return Ok(response);
    }

    [HttpGet]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response<ShopChangeRequestPageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] GetShopChangeRequestsQuery query, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopChangeRequestPageDto>>(query, ct);
        AddPagingHeaders(response.Data);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<ShopChangeRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopChangeRequestDto>>(
            new GetShopChangeRequestQuery(
                id,
                userContext.GetUserIdOrThrow(),
                userContext.HasAnyRole(RoleConsts.Moderator, RoleConsts.Admin)), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response<ShopChangeRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateShopChangeRequestBody body,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<ShopChangeRequestDto>>(
            new UpdateShopChangeRequestCommand(
                id, body.Section, body.Payload, userContext.GetUserIdOrThrow()), ct);
        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = RoleConsts.Moderator)]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Decide(
        Guid id,
        [FromBody] DecideShopChangeRequestBody body,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response>(
            new DecideShopChangeRequestCommand(
                id, body.Status, body.Comment, userContext.GetUserIdOrThrow()), ct);
        return response.IsSuccess
            ? Ok(response)
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    private void AddPagingHeaders(ShopChangeRequestPageDto? page)
    {
        if (page is null) return;
        Response.Headers.TryAdd("X-Total-Count", page.TotalItems.ToString());
        Response.Headers.TryAdd("X-Total-Pages", page.TotalPages.ToString());
        Response.Headers.TryAdd("X-Current-Page", page.CurrentPage.ToString());
        Response.Headers.TryAdd("X-Page-Size", page.PageSize.ToString());
    }
}
