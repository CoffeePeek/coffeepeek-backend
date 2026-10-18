using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.AccountService.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me/blocks")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthorBlocksController(IUserBlockStore blocks, IUserContext user) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int pageSize = 100, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Ok(Response<CoffeePeek.Contract.Dtos.Profiles.AuthorBlockPageDto>.Success(
            await blocks.ListAsync(user.GetUserIdOrThrow(), pageSize, cursor, ct)));

    [HttpPut("{authorId:guid}")]
    public async Task<IActionResult> Block(Guid authorId, CancellationToken ct) =>
        Ok(Response<CoffeePeek.Contract.Dtos.Profiles.AuthorBlockStateDto>.Success(
            await blocks.SetAsync(user.GetUserIdOrThrow(), authorId, true, ct)));

    [HttpDelete("{authorId:guid}")]
    public async Task<IActionResult> Unblock(Guid authorId, CancellationToken ct) =>
        Ok(Response<CoffeePeek.Contract.Dtos.Profiles.AuthorBlockStateDto>.Success(
            await blocks.SetAsync(user.GetUserIdOrThrow(), authorId, false, ct)));
}
