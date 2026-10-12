using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public record CreateShopChangeRequestCommand(
    Guid ShopId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShopChangeSection Section,
    ShopChangePayloadDto Payload)
{
    [JsonIgnore] public Guid UserId { get; init; }
}
