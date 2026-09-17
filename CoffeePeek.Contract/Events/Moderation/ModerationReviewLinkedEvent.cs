namespace CoffeePeek.Contract.Events.Moderation;

public record ModerationReviewLinkedEvent(Guid ShopId, Guid UserId, Guid ModerationReviewId);
