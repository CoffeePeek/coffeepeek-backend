using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize(Policy = RoleConsts.Moderator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCoffeeCatalogController(ICoffeeCatalogStore store, IMessageBus bus, IUserContext user) : ControllerBase
{
    [HttpGet("api/admin/coffees")]
    public async Task<IActionResult> List(CancellationToken ct, int page = 1, int pageSize = 20, string? status = null) =>
        Ok(Response<PublicPage<AdminCoffeeDto>>.Success(await store.AdminListAsync(page, pageSize, status, ct)));
    [HttpGet("api/admin/coffees/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    { var item = await store.AdminGetAsync(id, ct); return item is null ? NotFound() : Ok(Response<AdminCoffeeDto>.Success(item)); }
    [HttpPost("api/admin/coffees")]
    public async Task<IActionResult> Create(CreateCoffeeRequest request, CancellationToken ct) =>
        Ok(Response<AdminCoffeeDto>.Success(await bus.InvokeAsync<AdminCoffeeDto>(new CreateCoffeeCommand(request), ct)));
    [HttpPatch("api/admin/coffees/{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CoffeeEditRequest request, CancellationToken ct)
    {
        var item = await bus.InvokeAsync<AdminCoffeeDto?>(new EditCoffeeCommand(id, request), ct);
        return item is null ? NotFound() : Ok(Response<AdminCoffeeDto>.Success(item));
    }
    [HttpGet("api/admin/roaster-tags")]
    public async Task<IActionResult> Tags(CancellationToken ct) => Ok(Response<RoasterTagDto[]>.Success(await store.TagsAsync(true, ct)));
    [HttpDelete("api/admin/coffees/{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        await bus.InvokeAsync<bool>(new ArchiveCoffeeCommand(id), ct) ? NoContent() : NotFound();
    [HttpGet("api/admin/roasters/{id:guid}/tags")]
    public async Task<IActionResult> AssignedTags(Guid id, CancellationToken ct) =>
        Ok(Response<RoasterTagDto[]>.Success(await store.AdminRoasterTagsAsync(id, ct)));
    [HttpPost("api/admin/roaster-tags")]
    public async Task<IActionResult> CreateTag(CreateRoasterTagRequest request, CancellationToken ct) =>
        Ok(Response<RoasterTagDto>.Success(await bus.InvokeAsync<RoasterTagDto>(new CreateRoasterTagCommand(request), ct)));
    [HttpPatch("api/admin/roaster-tags/{id:guid}")]
    public async Task<IActionResult> EditTag(Guid id, RoasterTagEdit request, CancellationToken ct)
    {
        var item = await bus.InvokeAsync<RoasterTagDto?>(new EditRoasterTagCommand(id, request), ct);
        return item is null ? NotFound() : Ok(Response<RoasterTagDto>.Success(item));
    }
    [HttpDelete("api/admin/roaster-tags/{id:guid}")]
    public async Task<IActionResult> DeactivateTag(Guid id, CancellationToken ct)
    {
        var tag = (await store.TagsAsync(true, ct)).SingleOrDefault(t => t.Id == id);
        if (tag is null) return NotFound();
        return await EditTag(id, new(tag.Name, tag.Description, tag.SortOrder, false), ct);
    }
    [HttpPut("api/admin/roasters/{id:guid}/tags")]
    public async Task<IActionResult> SetTags(Guid id, SetRoasterTagsRequest request, CancellationToken ct)
    {
        await bus.InvokeAsync(new SetRoasterTagsCommand(id, request.TagIds, user.GetUserIdOrThrow()), ct);
        return NoContent();
    }
    [HttpGet("api/admin/coffee-import/runs")]
    public async Task<IActionResult> Runs(CancellationToken ct, int limit = 30) =>
        Ok(Response<CoffeeImportRunDto[]>.Success(await store.RunsAsync(limit, ct)));
}
