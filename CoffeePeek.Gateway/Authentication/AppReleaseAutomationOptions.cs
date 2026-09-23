namespace CoffeePeek.Gateway.Authentication;

public sealed class AppReleaseAutomationOptions
{
    public const string SectionName = "AppReleaseAutomation";

    public string? Token { get; set; }

    public Guid ActorUserId { get; set; } = Guid.Parse("52a6ea58-11c7-4a9f-b7d9-77bf842169d5");
}
