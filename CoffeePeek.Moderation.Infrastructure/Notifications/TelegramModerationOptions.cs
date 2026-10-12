namespace CoffeePeek.Moderation.Infrastructure.Notifications;

public sealed class TelegramModerationOptions
{
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public string RelayUrl { get; set; } = "";
}
