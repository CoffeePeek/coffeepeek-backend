using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Application.Features.Owner.ReorderPhotos;
using CoffeePeek.ShopsService.Controllers.Admin;
using CoffeePeek.ShopsService.Controllers.Owner;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Owner portal for managing assigned coffee shops.</summary>
[ApiController]
[Route("api/owner/coffee-shops")]
[Authorize(Policy = RoleConsts.Owner)]
[Tags("Owner")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class OwnerCoffeeShopsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<GetOwnerCoffeeShopsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyShops(CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var response = await bus.InvokeAsync<Response<GetOwnerCoffeeShopsResponse>>(
            new GetOwnerCoffeeShopsQuery(ownerId), ct);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyShop(Guid id, CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new GetOwnerCoffeeShopByIdQuery(id, ownerId), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMyShop(
        Guid id,
        [FromBody] UpdateOwnerCoffeeShopRequest request,
        CancellationToken ct,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (!ShopProfileRevisionHeader.TryParse(ifMatch, out var expectedRevision))
            return ShopMutationResult(Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest, "If-Match must contain a nonnegative profile revision."));

        var ownerId = userContext.GetUserIdOrThrow();
        var command = new UpdateOwnerCoffeeShopCommand(
            id,
            ownerId,
            request.Name,
            request.Description,
            request.PhoneNumber,
            request.Email,
            request.SiteLink,
            request.InstagramLink,
            request.Location is null
                ? null
                : new ShopLocationPatch(
                    request.Location.CityId,
                    request.Location.Address,
                    request.Location.Latitude,
                    request.Location.Longitude),
            request.Schedules,
            request.Catalogs is null
                ? null
                : new ShopCatalogsPatch(
                    request.Catalogs.EquipmentIds,
                    request.Catalogs.BeanIds,
                    request.Catalogs.RoasterIds,
                    request.Catalogs.BrewMethodIds))
        {
            ExpectedRevision = expectedRevision
        };

        try
        {
            var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(command, ct);
            return ShopMutationResult(response);
        }
        catch (NotFoundException ex)
        {
            return ShopMutationResult(Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, ex.Message));
        }
        catch (ValidationException ex)
        {
            return ShopMutationResult(Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.BadRequest, ex.Message));
        }
        catch (ConflictException ex)
        {
            return ShopMutationResult(Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.Conflict, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ShopMutationResult(Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.Conflict, "Shop profile changed; reload and retry."));
        }
    }

    /// <summary>Reorder gallery photos. Body must list every shop photo ID in the new display order (first = cover).</summary>
    [HttpPut("{id:guid}/photos/order")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReorderPhotos(
        Guid id,
        [FromBody] ReorderCoffeeShopPhotosRequest request,
        CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new ReorderOwnerCoffeeShopPhotosCommand(id, ownerId, request.PhotoIds), ct);

        return ShopMutationResult(response);
    }

    /// <summary>Attach already-uploaded gallery photos (presign via Media, then send storage keys here).</summary>
    [HttpPost("{id:guid}/photos")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddPhotos(
        Guid id,
        [FromBody] AddCoffeeShopPhotosRequest request,
        CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new AddPublishedShopPhotosCommand(id, ownerId, ownerId, request.Photos), ct);
        return ShopMutationResult(response);
    }

    /// <summary>Remove gallery photos by id. Remaining photos are reindexed.</summary>
    [HttpDelete("{id:guid}/photos")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePhotos(
        Guid id,
        [FromBody] RemoveCoffeeShopPhotosRequest request,
        CancellationToken ct)
    {
        var ownerId = userContext.GetUserIdOrThrow();
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new RemovePublishedShopPhotosCommand(id, ownerId, request.PhotoIds), ct);
        return ShopMutationResult(response);
    }

    private IActionResult ShopMutationResult(Response<AdminPublishedShopDto> response)
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
