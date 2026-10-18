using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using Microsoft.Extensions.Options;
using CoffeePeek.Shared.Kernel.Observability;

namespace CoffeePeek.Shops.Infrastructure.Account;

public sealed class AccountAuthorVisibilityLookup(IHttpClientFactory factory, IOptions<GatewayAuthOptions> options) : IAuthorVisibilityLookup
{
    // Deliberately not cached: every request observes the account service's committed policy.
    public async Task<Guid[]> ExcludedUserIdsAsync(Guid? viewerId, CancellationToken ct)
    {
        if (viewerId is null) return [];
        using var measurement = PhotoDeliveryTelemetry.Start("account", "authenticated");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/users/{viewerId:D}/excluded-authors");
        request.AddGatewayAuthHeader(options.Value.SecretKey);
        try
        {
            using var response = await factory.CreateClient("account-user-lookup").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new ServiceUnavailableException("Author visibility is unavailable.");
            var excluded = await response.Content.ReadFromJsonAsync<Guid[]>(cancellationToken: ct)
                ?? throw new ServiceUnavailableException("Invalid author visibility response.");
            measurement.Complete();
            return excluded;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException || error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new ServiceUnavailableException("Author visibility is unavailable."); }
    }
}
