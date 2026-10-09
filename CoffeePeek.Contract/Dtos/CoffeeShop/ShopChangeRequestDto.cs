using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopChangeRequestDto(
    Guid Id,
    Guid ShopId,
    Guid SubmittedByUserId,
    ShopChangeSection Section,
    ShopChangePayloadDto Payload,
    ModerationStatus Status,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
