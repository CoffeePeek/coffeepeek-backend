namespace CoffeePeek.Contract.Dtos.Notifications;

public sealed record NotificationTemplateDto(string Category, string Title, string Body, DateTime? UpdatedAtUtc);
