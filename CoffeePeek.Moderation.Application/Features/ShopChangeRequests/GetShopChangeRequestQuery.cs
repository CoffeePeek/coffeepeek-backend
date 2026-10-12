namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public record GetShopChangeRequestQuery(Guid RequestId, Guid UserId, bool IsModerator);
