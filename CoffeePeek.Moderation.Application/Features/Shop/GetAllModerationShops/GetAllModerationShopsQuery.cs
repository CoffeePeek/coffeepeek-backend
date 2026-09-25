using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.Shop.GetAllModerationShops;

public record GetAllModerationShopsQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    ModerationStatus? Status = null,
    CoffeeShopType? Type = null,
    [StringLength(100)] string? Search = null,
    Guid? UserId = null);
