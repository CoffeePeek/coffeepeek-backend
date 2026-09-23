using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Equipments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin/Moderator CRUD for the global equipment catalog.</summary>
[ApiController]
[Route("api/admin/equipments")]
[Authorize(Policy = RoleConsts.Moderator)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminEquipmentsController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<Response<EquipmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateEquipmentCommand command,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<EquipmentDto>>(command, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status409Conflict => Conflict(response),
            _ => BadRequest(response)
        };
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<Response<EquipmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateEquipmentRequest request,
        CancellationToken ct)
    {
        var command = new UpdateEquipmentCommand(id, request.Brand, request.ModelName, request.Category);
        var response = await bus.InvokeAsync<Response<EquipmentDto>>(command, ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<Response>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response>(new DeleteEquipmentCommand(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }
}

public record UpdateEquipmentRequest(string Brand, string ModelName, EquipmentCategoryEnum Category);
