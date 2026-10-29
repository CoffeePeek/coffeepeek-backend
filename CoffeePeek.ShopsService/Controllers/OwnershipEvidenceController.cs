using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Infrastructure.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OwnershipEvidenceController(IMessageBus bus, IUserContext user, OwnershipEvidenceClient media) : ControllerBase
{
    [HttpGet("api/owner/ownership-claims/{id:guid}/photos/{photoId:guid}")]
    public Task<IActionResult> Mine(Guid id, Guid photoId, CancellationToken ct) =>
        Download(id, photoId, user.GetUserIdOrThrow(), ct);

    [HttpGet("api/admin/ownership-claims/{id:guid}/photos/{photoId:guid}")]
    [Authorize(Policy = RoleConsts.Admin)]
    public Task<IActionResult> Admin(Guid id, Guid photoId, CancellationToken ct) => Download(id, photoId, null, ct);

    private async Task<IActionResult> Download(Guid id, Guid photoId, Guid? applicantId, CancellationToken ct)
    {
        var claim = await bus.InvokeAsync<Response<OwnershipClaimDto>>(new GetOwnershipClaimByIdQuery(id, applicantId), ct);
        if (!claim.IsSuccess || claim.Data == null || !claim.Data.EvidencePhotoIds.Contains(photoId)) return NotFound();
        using var response = await media.DownloadAsync(claim.Data.ApplicantUserId, id, photoId, ct);
        if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode == 404 ? 404 : 503);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        await response.Content.CopyToAsync(Response.Body, ct);
        return new EmptyResult();
    }
}
