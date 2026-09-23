using System.Net;

namespace CoffeePeek.Shops.Infrastructure.Menu;

internal static class OpenAiProxy
{
    public static IWebProxy? Create(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl))
            return null;

        if (!Uri.TryCreate(proxyUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "socks4" or "socks4a" or "socks5"))
        {
            throw new InvalidOperationException(
                "OpenAiOptions.ProxyUrl must be an absolute http, https, or socks5 URI, e.g. socks5://host:1080.");
        }

        var proxy = new WebProxy(uri);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(
                Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        return proxy;
    }

    public static string? DisplayHost(string? proxyUrl)
    {
        if (!Uri.TryCreate(proxyUrl?.Trim(), UriKind.Absolute, out var uri))
            return null;

        return string.IsNullOrEmpty(uri.UserInfo)
            ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped)
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
