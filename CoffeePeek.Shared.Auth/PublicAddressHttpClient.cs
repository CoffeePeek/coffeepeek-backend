using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shared.Auth;

// Service-to-service identity translation. No internal endpoint is routed by the gateway.
public sealed class PublicAddressHttpClient(IHttpClientFactory factory, IOptions<GatewayAuthOptions> options)
{
    public Task<PublicAddressMetadata?> ResolveAsync(string clientName, string kind, string slug, CancellationToken ct)
    {
        ValidateSlug(slug);
        return GetAsync<PublicAddressMetadata>(clientName, $"/internal/public-addresses/{kind}/by-slug/{Uri.EscapeDataString(slug)}", ct);
    }

    public async Task<Guid[]> ResolveIdsAsync(string clientName, string kind, string[] slugs, CancellationToken ct)
    {
        if (slugs.Length > 100) throw new ValidationException("Supply at most 100 public slugs.");
        if (slugs.Length == 0) return [];
        foreach (var slug in slugs) ValidateSlug(slug);
        var query = string.Join("&", slugs.Select(s => $"slugs={Uri.EscapeDataString(s)}"));
        return await GetAsync<Guid[]>(clientName, $"/internal/public-addresses/{kind}/resolve?{query}", ct)
            ?? throw new NotFoundException("Entity not found.");
    }

    private static void ValidateSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 100)
            throw new ValidationException("Supply a nonempty public slug of at most 100 characters.");
    }

    public async Task<PublicAddressMetadata[]> GetByIdsAsync(string clientName, string kind, Guid[] ids, CancellationToken ct)
    {
        var result = new List<PublicAddressMetadata>();
        foreach (var batch in ids.Where(id => id != Guid.Empty).Distinct().Chunk(100))
        {
            var query = string.Join("&", batch.Select(id => $"ids={id}"));
            result.AddRange(await GetAsync<PublicAddressMetadata[]>(clientName, $"/internal/public-addresses/{kind}?{query}", ct) ?? []);
        }
        return result.ToArray();
    }

    private async Task<T?> GetAsync<T>(string clientName, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.AddGatewayAuthHeader(options.Value.SecretKey);
        try
        {
            using var response = await factory.CreateClient(clientName).SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return default;
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new ValidationException("Invalid public address.");
            if (!response.IsSuccessStatusCode) throw new ServiceUnavailableException("Public address lookup is unavailable.");
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new ServiceUnavailableException("Invalid public address lookup response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ServiceUnavailableException("Public address lookup is unavailable.");
        }
    }
}
