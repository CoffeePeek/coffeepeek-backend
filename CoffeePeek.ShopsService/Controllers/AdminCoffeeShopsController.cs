using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Menu;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.Admin.Shops.ReorderPhotos;
using CoffeePeek.Shops.Application.Features.Admin.Shops.SetShopTags;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.ShopsService.Controllers.Admin;
using CoffeePeek.Contract.Dtos.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Admin management of published coffee shops.</summary>
[ApiController]
[Route("api/admin/shops")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class AdminCoffeeShopsController(IMessageBus bus, IUserContext userContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<Response<GetAdminCoffeeShopsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShops(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] DomainCoffeeShopStatus? status = null,
        [FromQuery] bool? importedFromFile = null,
        CancellationToken ct = default)
    {
        var query = new GetAdminCoffeeShopsQuery(page, pageSize, search, status, importedFromFile);
        var response = await bus.InvokeAsync<Response<GetAdminCoffeeShopsResponse>>(query, ct);

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
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShop(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new GetAdminCoffeeShopByIdQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UpdateShop(
        Guid id,
        [FromBody] UpdateAdminCoffeeShopRequest request,
        CancellationToken ct,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (!ShopProfileRevisionHeader.TryParse(ifMatch, out var expectedRevision))
            return ShopMutationResult(Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest, "If-Match must contain a nonnegative profile revision."));

        var command = new UpdateAdminCoffeeShopCommand(
            id,
            request.Name,
            request.Description,
            request.PriceRange,
            request.Status,
            request.Location is null
                ? null
                : new ShopLocationPatch(
                    request.Location.CityId,
                    request.Location.Address,
                    request.Location.Latitude,
                    request.Location.Longitude),
            request.Contacts is null
                ? null
                : new ShopContactsPatch(
                    request.Contacts.PhoneNumber,
                    request.Contacts.Email,
                    request.Contacts.SiteLink,
                    request.Contacts.InstagramLink),
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

    [HttpPatch("{id:guid}/visibility")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetVisibility(
        Guid id,
        [FromBody] SetCoffeeShopVisibilityRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new SetAdminCoffeeShopVisibilityCommand(id, request.Hidden), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPatch("{id:guid}/owner")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignOwner(
        Guid id,
        [FromBody] AssignCoffeeShopOwnerRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new AssignCoffeeShopOwnerCommand(id, request.OwnerUserId), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
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
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new ReorderAdminCoffeeShopPhotosCommand(id, request.PhotoIds), ct);

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
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new AddPublishedShopPhotosCommand(id, userContext.GetUserIdOrThrow(), null, request.Photos), ct);
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
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new RemovePublishedShopPhotosCommand(id, null, request.PhotoIds), ct);
        return ShopMutationResult(response);
    }

    [HttpPatch("{id:guid}/focus")]
    [ProducesResponseType<Response<AdminPublishedShopDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetFocus(
        Guid id,
        [FromBody] SetCoffeeShopFocusRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(
            new SetAdminCoffeeShopFocusCommand(id, request.Type, userContext.GetUserIdOrThrow()), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    /// <summary>Replace the full set of filter tags assigned to a shop.</summary>
    [HttpPut("{shopId:guid}/tags")]
    [ProducesResponseType(typeof(Response), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetTags(
        Guid shopId,
        [FromBody] SetCoffeeShopTagsRequest request,
        CancellationToken ct)
    {
        var command = new SetShopTagsCommand(
            shopId,
            request.TagIds ?? [],
            userContext.GetUserIdOrThrow());

        var response = await bus.InvokeAsync<Response>(command, ct);
        if (response.IsSuccess)
            return Ok(response);

        return response.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(response),
            _ => BadRequest(response)
        };
    }

    [HttpGet("{id:guid}/menu")]
    [ProducesResponseType<Response<AdminShopMenuDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMenu(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopMenuDto>>(new GetAdminShopMenuQuery(id), ct);
        return response.IsSuccess ? Ok(response) : NotFound(response);
    }

    [HttpPost("{id:guid}/menu/photos")]
    [ProducesResponseType<Response<AdminShopMenuDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AttachMenuPhotos(
        Guid id,
        [FromBody] AttachMenuPhotosRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopMenuDto>>(
            new AttachAdminShopMenuPhotosCommand(id, request.Photos, userContext.GetUserIdOrThrow()), ct);
        return MenuActionResult(response);
    }

    /// <summary>Remove photos from a menu by id. The menu itself and its items remain available.</summary>
    [HttpDelete("{id:guid}/menu/photos")]
    [ProducesResponseType<Response<AdminShopMenuDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMenuPhotos(
        Guid id,
        [FromBody] RemoveMenuPhotosRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopMenuDto>>(
            new RemoveAdminShopMenuPhotosCommand(id, request.PhotoIds), ct);
        return MenuActionResult(response);
    }

    [HttpPost("{id:guid}/menu/parse")]
    [ProducesResponseType<Response<AdminShopMenuDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ParseMenu(Guid id, CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopMenuDto>>(
            new ParseAdminShopMenuCommand(id, userContext.GetUserIdOrThrow()), ct);
        return MenuActionResult(response);
    }

    [HttpPut("{id:guid}/menu")]
    [ProducesResponseType<Response<AdminShopMenuDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMenu(
        Guid id,
        [FromBody] UpdateShopMenuRequest request,
        CancellationToken ct)
    {
        var response = await bus.InvokeAsync<Response<AdminShopMenuDto>>(
            new UpdateAdminShopMenuCommand(
                id, request.Items, request.ApplySuggestedPriceRange, userContext.GetUserIdOrThrow()),
            ct);
        return MenuActionResult(response);
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

    private IActionResult MenuActionResult(Response<AdminShopMenuDto> response)
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
