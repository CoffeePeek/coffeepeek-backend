namespace CoffeePeek.Contract.Dtos.Notifications;

public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, Guid? NextCursor, int UnreadCount);
