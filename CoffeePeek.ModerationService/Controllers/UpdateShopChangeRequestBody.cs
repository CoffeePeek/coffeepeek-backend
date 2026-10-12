using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.ModerationService.Controllers;

public record UpdateShopChangeRequestBody(ShopChangeSection Section, ShopChangePayloadDto Payload);
