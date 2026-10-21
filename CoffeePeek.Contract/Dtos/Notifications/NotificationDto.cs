namespace CoffeePeek.Contract.Dtos.Notifications;

public sealed record NotificationDto(Guid Id, string Type, string Title, string Body,
    DateTime CreatedAtUtc, DateTime? ReadAtUtc, string? EntityType, Guid? EntityId, string? Status);
