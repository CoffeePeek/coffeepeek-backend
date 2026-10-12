using CoffeePeek.Contract.Enums;

namespace CoffeePeek.ModerationService.Controllers;

public record DecideShopChangeRequestBody(ModerationStatus Status, string? Comment);
