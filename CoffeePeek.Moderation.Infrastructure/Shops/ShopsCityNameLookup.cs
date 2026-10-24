using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Infrastructure.Shops;

public sealed class ShopsCityNameLookup(
    IHttpClientFactory clients,
    IOptions<GatewayAuthOptions> options) : ICityNameLookup
{
    public async Task<string?> GetNameAsync(Guid cityId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/cities/{cityId:D}");
        request.AddGatewayAuthHeader(options.Value.SecretKey);
        try
        {
            using var response = await clients.CreateClient("shops-roaster-lookup").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var city = await response.Content.ReadFromJsonAsync<CityDto>(cancellationToken: ct);
            if (city is null || city.Id != cityId || string.IsNullOrWhiteSpace(city.Name))
                throw new HttpRequestException("City lookup returned an invalid city.");
            return city.Name.Trim();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("City lookup timed out.", ex);
        }
    }
}
