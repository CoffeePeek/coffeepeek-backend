using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public record UpdateShopChangeRequestCommand(
    Guid RequestId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShopChangeSection Section,
    ShopChangePayloadDto Payload,
    [property: JsonIgnore] Guid ModeratorUserId);
