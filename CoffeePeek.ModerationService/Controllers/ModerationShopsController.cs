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
using CoffeePeek.Shared.Kernel.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Moderation.Application.Services;
using Mapster;

namespace CoffeePeek.ModerationService.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/[controller]")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class ModerationShopsController(IMessageBus bus, IUserContext userContext, PublicModerationService discovery) : ControllerBase
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
            AddPagingHeaders(response.Data.TotalItems, response.Data.TotalPages, response.Data.CurrentPage, response.Data.PageSize);

        return Ok(response);
    }

    /// <summary>
    /// Lists the caller's own coffee shop submissions, newest first. The submitter filter comes only from the
    /// authenticated user; the query string cannot select another user's items.
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    [Description("Get the caller's own coffee shop submissions with moderation status")]
    [ProducesResponseType<Response<PublicPage<PublicModerationShopDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        [FromQuery] ModerationStatus? status = null,
        CancellationToken ct = default)
    {
        var query = new GetAllModerationShopsQuery(page, pageSize, status, UserId: userContext.GetUserIdOrThrow());
        var response = await bus.InvokeAsync<Response<GetAllModerationShopsResponse>>(query, ct);

        if (response.IsSuccess && response.Data is not null)
            AddPagingHeaders(response.Data.TotalItems, response.Data.TotalPages, response.Data.CurrentPage, response.Data.PageSize);

        if (response is not { IsSuccess: true, Data: not null }) return BadRequest(response);
        var data = response.Data;
        return Ok(Response<PublicPage<PublicModerationShopDto>>.Success(new(await discovery.ShopsAsync(data.ModerationShops, ct),
            data.TotalItems, data.TotalPages, data.CurrentPage, data.PageSize)));
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
        [FromBody] PublicSubmitShopRequest request, CancellationToken ct)
    {
        var cityTask = WriteStageTrace.MeasureAsync("shop_submit", "city_lookup",
            () => discovery.ResolveIdAsync("cities", request.City, ct));
        var equipmentTask = ResolveOptionalAsync("equipments", request.Equipments, ct);
        var beansTask = ResolveOptionalAsync("beans", request.Beans, ct);
        var roastersTask = ResolveOptionalAsync("roasters", request.Roasters, ct);
        var methodsTask = ResolveOptionalAsync("brew-methods", request.BrewMethods, ct);
        await Task.WhenAll(cityTask, equipmentTask, beansTask, roastersTask, methodsTask);

        // Anonymous submissions carry a null UserId; approval consumer skips user crediting for these.
        var commandWithUser = request.Adapt<SendCoffeeShopToModerationCommand>() with
        {
            UserId = userContext.GetUserId(), CityId = await cityTask,
            EquipmentIds = (await equipmentTask)?.ToList(),
            CoffeeBeanIds = (await beansTask)?.ToList(),
            RoasterIds = (await roastersTask)?.ToList(),
            BrewMethodIds = (await methodsTask)?.ToList()
        };

        var response = await WriteStageTrace.MeasureAsync("shop_submit", "commit",
            () => bus.InvokeAsync<Response<SendCoffeeShopToModerationResponse>>(commandWithUser, ct));

        if (!response.IsSuccess)
            return BadRequest(response);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    private Task<Guid[]?> ResolveOptionalAsync(string kind, string[]? slugs, CancellationToken ct) =>
        slugs is null
            ? Task.FromResult<Guid[]?>(null)
            : WriteStageTrace.MeasureAsync<Guid[]?>("shop_submit", $"{kind}_lookup",
                async () => await discovery.ResolveIdsAsync(kind, slugs, ct));

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
                id, request.Items, request.ApplySuggestedPriceRange, userContext.GetUserIdOrThrow(), request.ExpectedInputRevision),
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

    private void AddPagingHeaders(int totalItems, int totalPages, int currentPage, int pageSize)
    {
        Response.Headers.TryAdd("X-Total-Count", totalItems.ToString());
        Response.Headers.TryAdd("X-Total-Pages", totalPages.ToString());
        Response.Headers.TryAdd("X-Current-Page", currentPage.ToString());
        Response.Headers.TryAdd("X-Page-Size", pageSize.ToString());
    }
}
