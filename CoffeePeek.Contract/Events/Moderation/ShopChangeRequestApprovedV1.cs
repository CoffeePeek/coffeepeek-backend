using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Events.Moderation;

public record ShopChangeRequestApprovedV1(
    Guid RequestId,
    Guid ShopId,
    Guid SubmittedByUserId,
    ShopChangeSection Section,
    ShopChangePayloadDto Payload,
    Guid ApprovedByUserId,
    DateTime ApprovedAtUtc);
