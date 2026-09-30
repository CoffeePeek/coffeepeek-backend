using System.Net.Http.Json;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Account;

public class AccountUserExistenceLookup(
    IHttpClientFactory httpClientFactory,
    IOptions<GatewayAuthOptions> gatewayAuthOptions,
    ILogger<AccountUserExistenceLookup> logger) : IUserExistenceLookup
{
    public async Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            return false;

        var client = httpClientFactory.CreateClient("account-user-lookup");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/{userId}");
        request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Account user lookup for {UserId} failed with {StatusCode}",
                    userId,
                    response.StatusCode);
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<Response>(cancellationToken: ct);
            return payload?.IsSuccess == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Account user lookup for {UserId} failed", userId);
            return false;
        }
    }
}
