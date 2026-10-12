namespace CoffeePeek.Moderation.Infrastructure.Notifications;

public sealed class TelegramModerationOptions
{
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public string RelayUrl { get; set; } = "";
    public Guid ModeratorUserId { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.coffeepeek.by";
    public string AdminBaseUrl { get; set; } = "https://platform-admin.coffeepeek.by";
}
