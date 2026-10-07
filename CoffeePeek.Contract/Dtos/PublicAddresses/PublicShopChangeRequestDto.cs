using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicShopChangeRequestDto(Guid Id, PublicAddressDto? Shop, ShopChangeSection Section,
    PublicShopChangePayloadDto Payload, ModerationStatus Status, DateTime? ReviewedAtUtc, string? RejectionReason,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
