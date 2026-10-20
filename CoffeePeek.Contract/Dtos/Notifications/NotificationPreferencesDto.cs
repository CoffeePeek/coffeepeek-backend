namespace CoffeePeek.Contract.Dtos.Notifications;

public sealed record NotificationPreferencesDto(bool Moderation = true, bool Inactivity = false, bool Custom = true);
