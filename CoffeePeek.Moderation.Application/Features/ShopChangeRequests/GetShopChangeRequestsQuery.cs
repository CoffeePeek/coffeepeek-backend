using CoffeePeek.Contract.Enums;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public record GetShopChangeRequestsQuery(
    int Page = 1,
    int PageSize = 20,
    ModerationStatus? Status = null,
    Guid? ShopId = null,
    ShopChangeSection? Section = null,
    Guid? SubmittedByUserId = null);
