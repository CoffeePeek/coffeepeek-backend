namespace CoffeePeek.Moderation.Application.Features.Admin.Audit;

public record GetModerationAuditLogResponse(
    IReadOnlyList<ModerationAuditLogDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
