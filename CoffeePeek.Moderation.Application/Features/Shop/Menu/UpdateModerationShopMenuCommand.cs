using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using DomainPriceRange = CoffeePeek.Moderation.Domain.Aggregates.Enums.PriceRange;

namespace CoffeePeek.Moderation.Application.Features.Shop.Menu;

public record UpdateModerationShopMenuCommand(
    Guid ShopId,
    IReadOnlyList<UpdateShopMenuItemRequest> Items,
    bool ApplySuggestedPriceRange,
    Guid UserId,
    long? ExpectedInputRevision = null);
