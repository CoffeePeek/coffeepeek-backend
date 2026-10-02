using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Moderation.Application.Services;

namespace CoffeePeek.ModerationService.Controllers;

public record UpdateShopChangeRequestBody(ShopChangeSection Section, ShopChangePayloadDto Payload);
public record DecideShopChangeRequestBody(ModerationStatus Status, string? Comment);

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/ShopChangeRequests")]
[Authorize]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ShopChangeRequestsController(IMessageBus bus, IUserContext userContext, PublicModerationService discovery) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<PublicShopChangeRequestDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] PublicCreateShopChangeRequest request,
        CancellationToken ct)
    {
        var shopTask = WriteStageTrace.MeasureAsync("shop_change", "shop_lookup",
            () => discovery.ResolveIdAsync("shops", request.Shop, ct));
        var payloadTask = WriteStageTrace.MeasureAsync("shop_change", "payload_lookup",
            () => discovery.PayloadAsync(request.Payload, ct));
        await Task.WhenAll(shopTask, payloadTask);
        var command = new CreateShopChangeRequestCommand(await shopTask,
            request.Section, await payloadTask) { UserId = userContext.GetUserIdOrThrow() };
        var response = await WriteStageTrace.MeasureAsync("shop_change", "commit",
            () => bus.InvokeAsync<Response<ShopChangeRequestDto>>(command, ct));
        return response is { IsSuccess: true, Data: not null }
            ? StatusCode(StatusCodes.Status201Created, Response<PublicShopChangeRequestDto>.Success((await WriteStageTrace.MeasureAsync(
                "shop_change", "response_enrichment", () => discovery.ChangesAsync([response.Data], ct)))[0]))
            : StatusCode(response.StatusCode ?? StatusCodes.Status400BadRequest, response);
    }

    [HttpGet("mine")]
    [ProducesResponseType<Response<PublicPage<PublicShopChangeRequestDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ModerationStatus? status = null,
        [FromQuery] string? shop = null,
        [FromQuery] ShopChangeSection? section = null,
        CancellationToken ct = default)
    {
        var response = await bus.InvokeAsync<Response<ShopChangeRequestPageDto>>(
            new GetMyShopChangeRequestsQuery(
                userContext.GetUserIdOrThrow(), page, pageSize, status,
                shop is null ? null : await discovery.ResolveIdAsync("shops", shop, ct), section), ct);
        AddPagingHeaders(response.Data);
        if (response is not { IsSuccess: true, Data: not null }) return BadRequest(response);
        var data = response.Data;
        return Ok(Response<PublicPage<PublicShopChangeRequestDto>>.Success(new(await discovery.ChangesAsync(data.Items, ct),
            data.TotalItems, data.TotalPages, data.CurrentPage, data.PageSize)));
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
        if (response is not { IsSuccess: true, Data: not null }) return NotFound(response);
        return userContext.HasAnyRole(RoleConsts.Moderator, RoleConsts.Admin) ? Ok(response)
            : Ok(Response<PublicShopChangeRequestDto>.Success((await discovery.ChangesAsync([response.Data], ct))[0]));
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
