using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.AccountService.Controllers;

/// <summary>
/// D-10: bounded backfill of profile addresses, the only admin address surface for users. Always start with
/// <c>dryRun=true</c>. There is no initialize, rename or history for users (D-06): a user's address follows their
/// username. The gateway <c>account-admin-route</c> already requires Admin; this attribute is authoritative in the service.
/// </summary>
[ApiController]
[Route("api/admin/users/public-addresses")]
[Authorize(Policy = RoleConsts.Admin)]
[Tags("Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public sealed class AdminUserPublicAddressesController(IProfileAddressService addresses) : ControllerBase
{
    /// <summary>
    /// Allocates missing canonical addresses for non-deleted users (blocked included), paged by user id after
    /// <paramref name="afterId"/>. A dry run writes nothing; a replay never renames and reports alreadyInitialized.
    /// </summary>
    [HttpPost("backfill")]
    [ProducesResponseType<PublicAddressBackfillResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicAddressBackfillResult>> Backfill([FromQuery] Guid? afterId = null,
        [FromQuery] int limit = 50, [FromQuery] bool dryRun = true, CancellationToken ct = default) =>
        Ok(await addresses.BackfillAsync(afterId, limit, dryRun, ct));
}
