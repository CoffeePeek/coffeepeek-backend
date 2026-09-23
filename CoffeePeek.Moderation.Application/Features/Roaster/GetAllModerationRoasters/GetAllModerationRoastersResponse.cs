using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Moderation.Application.Features.Roaster.GetAllModerationRoasters;

public record GetAllModerationRoastersResponse(
    ModerationRoasterDto[] Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
