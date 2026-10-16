namespace CoffeePeek.Account.Infrastructure;

public sealed class OAuthAppleOptions
{
    public string ClientId { get; set; } = "com.coffeepeek";
    public string ServiceId { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}
