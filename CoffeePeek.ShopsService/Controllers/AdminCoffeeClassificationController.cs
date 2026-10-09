using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize(Policy = RoleConsts.Moderator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCoffeeClassificationController(IDiscoveryClassificationStore store, IMessageBus bus) : ControllerBase
{
    [HttpGet("api/admin/coffee-filter-values")]
    public async Task<IActionResult> Values(CancellationToken ct) =>
        Ok(Response<AdminCoffeeFilterValue[]>.Success(await store.AdminValuesAsync(ct)));

    [HttpPost("api/admin/coffee-filter-values")]
    public async Task<IActionResult> Create(CreateCoffeeFilterValueRequest request, CancellationToken ct) =>
        Ok(Response<AdminCoffeeFilterValue>.Success(await bus.InvokeAsync<AdminCoffeeFilterValue>(new CreateCoffeeFilterValueCommand(request), ct)));

    [HttpPatch("api/admin/coffee-filter-values/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, EditCoffeeFilterValueRequest request, CancellationToken ct)
    {
        var item = await bus.InvokeAsync<AdminCoffeeFilterValue?>(new EditCoffeeFilterValueCommand(id, request), ct);
        return item is null ? NotFound(new ErrorResponse("Coffee filter value not found.", "NOT_FOUND") { StatusCode = 404 }) : Ok(Response<AdminCoffeeFilterValue>.Success(item));
    }

    [HttpDelete("api/admin/coffee-filter-values/{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var item = await bus.InvokeAsync<AdminCoffeeFilterValue?>(new DeactivateCoffeeFilterValueCommand(id), ct);
        return item is null ? NotFound(new ErrorResponse("Coffee filter value not found.", "NOT_FOUND") { StatusCode = 404 }) : Ok(Response<AdminCoffeeFilterValue>.Success(item));
    }

    [HttpGet("api/admin/coffees/{id:guid}/classification")]
    public async Task<IActionResult> Classification(Guid id, CancellationToken ct)
    {
        var item = await store.GetClassificationAsync(id, ct);
        return item is null ? NotFound(new ErrorResponse("Coffee or variant not found.", "NOT_FOUND") { StatusCode = 404 }) : Ok(Response<AdminCoffeeClassification>.Success(item));
    }

    [HttpPatch("api/admin/coffees/{id:guid}/classification")]
    public async Task<IActionResult> EditClassification(Guid id, CoffeeClassificationEditRequest request, CancellationToken ct)
    {
        var item = await bus.InvokeAsync<AdminCoffeeClassification?>(new EditCoffeeClassificationCommand(id, request), ct);
        return item is null ? NotFound(new ErrorResponse("Coffee or variant not found.", "NOT_FOUND") { StatusCode = 404 }) : Ok(Response<AdminCoffeeClassification>.Success(item));
    }

    [HttpPatch("api/admin/coffees/{id:guid}/variants/{variantId:guid}/classification")]
    public async Task<IActionResult> EditVariantClassification(Guid id, Guid variantId, VariantClassificationEditRequest request, CancellationToken ct)
    {
        var item = await bus.InvokeAsync<AdminCoffeeClassification?>(new EditCoffeeVariantClassificationCommand(id, variantId, request), ct);
        return item is null ? NotFound(new ErrorResponse("Coffee or variant not found.", "NOT_FOUND") { StatusCode = 404 }) : Ok(Response<AdminCoffeeClassification>.Success(item));
    }
}
