using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Moderation.Infrastructure.Shops;

public class ShopsRoasterExistenceLookup(
    IHttpClientFactory httpClientFactory,
    ILogger<ShopsRoasterExistenceLookup> logger) : IRoasterExistenceLookup
{
    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var client = httpClientFactory.CreateClient("shops-roaster-lookup");

        try
        {
            var response = await client.GetFromJsonAsync<Response<RoasterListResponse>>(
                "/api/catalogs/roasters", ct);

            return response?.Data?.Roasters?.Any(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Roaster existence lookup for {Name} failed", name);
            return false;
        }
    }

    private record RoasterListResponse(RoasterDto[] Roasters);
}
