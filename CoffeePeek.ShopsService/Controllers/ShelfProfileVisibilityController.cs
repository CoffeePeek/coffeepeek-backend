using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Application.Features.Shelf.SetProfileVisibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController, Authorize, Route("api/v1/shelf/profile-visibility")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShelfProfileVisibilityController(IShelfProfileStore profiles, IUserContext user, IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(Response<ShelfProfileVisibilityDto>.Success(await profiles.VisibilityAsync(user.GetUserIdOrThrow(), ct)));

    [HttpPut]
    public async Task<IActionResult> Set(SetShelfProfileVisibilityRequest request, CancellationToken ct) =>
        Ok(Response<ShelfProfileVisibilityDto>.Success(await bus.InvokeAsync<ShelfProfileVisibilityDto>(
            new SetShelfProfileVisibilityCommand(user.GetUserIdOrThrow(), request.IsVisible), ct)));
}
