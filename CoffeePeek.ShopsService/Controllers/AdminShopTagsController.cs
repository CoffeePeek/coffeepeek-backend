using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.ShopTags;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin/Moderator CRUD for the global shop filter-tag catalog.</summary>
[ApiController]
[Route("api/admin/shop-tags")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminShopTagsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<AdminShopTagDto[]>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopTagDto[]>>(
            new GetAllAdminShopTagsQuery(), ct);
        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType<Response<AdminShopTagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateShopTagCommand command,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopTagDto>>(command, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status409Conflict => Conflict(response),
            _ => BadRequest(response)
        };
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Response<AdminShopTagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateShopTagRequest request,
        CancellationToken ct)
    {
        var command = new UpdateShopTagCommand(
            id, request.Name, request.Description, request.SortOrder, request.IsActive);
        var response = await bus.InvokeAsync<Response<AdminShopTagDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    /// <summary>Soft-deactivates a tag (IsActive = false). Slug remains reserved.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<Response<AdminShopTagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopTagDto>>(
            new DeactivateShopTagCommand(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}

public record UpdateShopTagRequest(
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive);
