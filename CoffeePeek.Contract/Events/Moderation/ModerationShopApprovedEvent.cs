using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Contract.Events.Moderation;

public record ModerationShopApprovedEvent(Guid? UserId, ShopDto Shop);