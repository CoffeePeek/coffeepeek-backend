using System.Net.Http.Json;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Infrastructure.Shops;

public class ShopsRoasterExistenceLookup(
    IHttpClientFactory httpClientFactory,
    IOptions<GatewayAuthOptions> gatewayAuthOptions,
    ILogger<ShopsRoasterExistenceLookup> logger) : IRoasterExistenceLookup
{
    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var client = httpClientFactory.CreateClient("shops-roaster-lookup");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/internal/roasters/exists?name={Uri.EscapeDataString(name.Trim())}");
            request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<bool>(cancellationToken: ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Roaster existence lookup for {Name} failed", name);
            return false;
        }
    }

}
