using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/coffee-import")]
public sealed class InternalCoffeeImportController(IMessageBus bus, IOptions<CoffeeImportAuthOptions> auth) : ControllerBase
{
    [HttpPost("failures")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Failure(CoffeeImportFailureDto failure, CancellationToken ct)
    {
        if (auth.Value.Token.Length < 32 || !GatewayAuthVerifier.IsTrusted(Request.Headers[CoffeeImportAuthOptions.Header], auth.Value.Token)) return Unauthorized();
        await bus.InvokeAsync(new RecordCoffeeImportFailureCommand(failure), ct);
        return Ok(new { status = "recorded" });
    }
    [HttpPost("snapshots")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> Import(CoffeeSnapshotImportRequest request, CancellationToken ct)
    {
        if (auth.Value.Token.Length < 32 || !GatewayAuthVerifier.IsTrusted(Request.Headers[CoffeeImportAuthOptions.Header], auth.Value.Token)) return Unauthorized();
        return Ok(await bus.InvokeAsync<CoffeeImportResult>(new ImportCoffeeSnapshotCommand(request), ct));
    }
}
