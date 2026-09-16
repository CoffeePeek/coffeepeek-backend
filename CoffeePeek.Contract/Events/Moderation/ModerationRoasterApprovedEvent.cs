using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Contract.Events.Moderation;

public record ModerationRoasterApprovedEvent(Guid? UserId, ModerationRoasterDto Roaster);
