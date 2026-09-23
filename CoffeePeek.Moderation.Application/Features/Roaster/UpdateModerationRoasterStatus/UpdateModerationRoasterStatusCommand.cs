using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Moderation.Application.Features.Roaster.UpdateModerationRoasterStatus;

public record UpdateModerationRoasterStatusCommand(
    Guid UserId,
    Guid Id,
    ModerationStatus ModerationStatus,
    string? Comment);
