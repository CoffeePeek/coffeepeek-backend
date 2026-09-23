using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shared.Kernel.Options;

public class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4.1-mini";

    [Url]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    [Range(5, 180)]
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Optional outbound HTTP/HTTPS/SOCKS proxy for OpenAI calls.
    /// The API rejects some egress regions; set this to a proxy whose exit IP is supported.
    /// Example: <c>socks5://user:pass@proxy.example:1080</c>.
    /// </summary>
    public string ProxyUrl { get; set; } = string.Empty;
}
