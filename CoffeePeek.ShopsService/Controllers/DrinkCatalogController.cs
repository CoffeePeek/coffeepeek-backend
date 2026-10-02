using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.Drinks;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace CoffeePeek.ShopsService.Controllers;

/// <summary>Public consumed-drink choices and administrator catalog management.</summary>
/// <param name="bus">Application command and query dispatcher.</param>
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesErrorResponseType(typeof(ErrorResponse))]
public class DrinkCatalogController(IMessageBus bus) : ControllerBase
{
    /// <summary>Active drinks for reviews and check-ins, including Other.</summary>
    [AllowAnonymous]
    [HttpGet("api/catalogs/drinks")]
    [ProducesResponseType<Response<ConsumedDrinkOptionDto[]>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDrinks(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<ConsumedDrinkOptionDto[]>>(new GetConsumedDrinksQuery(), ct));

    /// <summary>All catalog entries, including inactive drinks.</summary>
    [Authorize(Policy = RoleConsts.Admin)]
    [HttpGet("api/admin/drinks")]
    [ProducesResponseType<Response<AdminDrinkDto[]>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdminDrinks(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<AdminDrinkDto[]>>(new GetAdminDrinksQuery(), ct));

    /// <summary>Create a drink with a stable, unique slug. Other is reserved.</summary>
    [Authorize(Policy = RoleConsts.Admin)]
    [HttpPost("api/admin/drinks")]
    [ProducesResponseType<Response<AdminDrinkDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(CreateCatalogDrinkRequest request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<AdminDrinkDto>>(new CreateCatalogDrinkCommand(request.Slug,
            request.NameRu, request.NameEn, request.Category, request.Aliases, request.SortOrder, request.IsActive), ct));

    /// <summary>Edit names, category, aliases and order, or deactivate a drink. Slugs are immutable.</summary>
    [Authorize(Policy = RoleConsts.Admin)]
    [HttpPut("api/admin/drinks/{slug}")]
    [ProducesResponseType<Response<AdminDrinkDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string slug, UpdateCatalogDrinkRequest request, CancellationToken ct) =>
        Ok(await bus.InvokeAsync<Response<AdminDrinkDto>>(new UpdateCatalogDrinkCommand(slug,
            request.NameRu, request.NameEn, request.Category, request.Aliases, request.SortOrder, request.IsActive), ct));
}

/// <summary>Editable catalog fields; set IsActive to false to retire a choice.</summary>
public record UpdateCatalogDrinkRequest
{
    /// <summary>Russian display name.</summary>
    [Required, MaxLength(100)] public string NameRu { get; init; } = null!;
    /// <summary>English display name.</summary>
    [Required, MaxLength(100)] public string NameEn { get; init; } = null!;
    /// <summary>Espresso (1) or filter (2).</summary>
    public CoffeeDrinkCategory Category { get; init; }
    /// <summary>Comma-separated matching aliases for menu recognition.</summary>
    [Required(AllowEmptyStrings = true), MaxLength(500)] public string Aliases { get; init; } = string.Empty;
    /// <summary>Nonnegative display order.</summary>
    [Range(0, int.MaxValue)] public int SortOrder { get; init; }
    /// <summary>Whether users can select this drink for new entries.</summary>
    public bool IsActive { get; init; } = true;
}

/// <summary>New catalog entry with a stable unique slug.</summary>
public record CreateCatalogDrinkRequest : UpdateCatalogDrinkRequest
{
    /// <summary>Immutable identifier; other is reserved.</summary>
    [Required, MaxLength(50)] public string Slug { get; init; } = null!;
}
