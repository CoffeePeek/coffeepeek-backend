using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeePeek.ShopsService.Controllers;

[ApiController, Route("api/v1/profiles/{authorSlug}/collection")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileCollectionController(IProfileAddressLookup profiles, ISavedCollectionsStore collections) : ControllerBase
{
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Get(string authorSlug, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(authorSlug, ct);
        if (profile == null) return NotFound();
        return Ok(Response<SavedCollectionDto?>.Success(await collections.PublicFeaturedAsync(profile.EntityId, ct)));
    }
}
