using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Account.Application.Features.Admin.Stats;
using CoffeePeek.Account.Infrastructure.Options;
using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Admin;

public class AdminStatsClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    IOptions<AdminStatsOptions> options,
    IOptions<GatewayAuthOptions> gatewayAuthOptions,
    ILogger<AdminStatsClient> logger) : IAdminStatsClient
{
    public async Task<AdminPlatformStatsSnapshot> GetPlatformStatsAsync(CancellationToken cancellationToken = default)
    {
        var shopsTask = TryFetchAsync(
            options.Value.ShopsServiceUrl,
            AppResources.ShopsService,
            cancellationToken);
        var moderationTask = TryFetchAsync(
            options.Value.ModerationServiceUrl,
            AppResources.ModerationService,
            cancellationToken);

        await Task.WhenAll(shopsTask, moderationTask);

        var shops = await shopsTask;
        var moderation = await moderationTask;

        return new AdminPlatformStatsSnapshot(
            TotalCoffeeShops: shops?.TotalCoffeeShops ?? 0,
            TotalCheckIns: shops?.TotalCheckIns ?? 0,
            NewCoffeeShopsToday: shops?.NewCoffeeShopsToday ?? 0,
            NewCheckInsToday: shops?.NewCheckInsToday ?? 0,
            PendingModerationShops: moderation?.PendingModerationShops ?? 0,
            PendingModerationCheckIns: moderation?.PendingModerationCheckIns ?? 0,
            ImportPending: moderation?.ImportPending ?? 0,
            ImportPublished: moderation?.ImportPublished ?? 0,
            ImportRejected: moderation?.ImportRejected ?? 0,
            ImportSkipped: moderation?.ImportSkipped ?? 0,
            ImportInFeed: moderation?.ImportInFeed ?? 0,
            ShopsAvailable: shops is not null,
            ModerationAvailable: moderation is not null);
    }

    private async Task<AdminServiceStatsDto?> TryFetchAsync(
        string? configuredBaseUrl,
        string aspireServiceName,
        CancellationToken cancellationToken)
    {
        var baseUrl = ResolveBaseUrl(configuredBaseUrl, aspireServiceName);
        try
        {
            var client = httpClientFactory.CreateClient("admin-stats");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/api/admin/stats/summary");
            request.AddGatewayAuthHeader(gatewayAuthOptions.Value.SecretKey);
            ForwardAuthHeaders(request);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Admin stats request to {BaseUrl} failed with {StatusCode}",
                    baseUrl,
                    response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<Response<AdminServiceStatsDto>>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);

            if (payload is { IsSuccess: true, Data: not null })
                return payload.Data;

            logger.LogWarning("Admin stats request to {BaseUrl} returned an invalid payload", baseUrl);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin stats request to {BaseUrl} failed", baseUrl);
            return null;
        }
    }

    private static string ResolveBaseUrl(string? configuredBaseUrl, string aspireServiceName)
    {
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            return configuredBaseUrl.TrimEnd('/');

        return $"http://{aspireServiceName}";
    }

    private void ForwardAuthHeaders(HttpRequestMessage request)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return;

        foreach (var headerName in new[]
                 {
                     GatewayHeaderConsts.XUserId,
                     GatewayHeaderConsts.XUserName,
                     GatewayHeaderConsts.XUserRole
                 })
        {
            if (httpContext.Request.Headers.TryGetValue(headerName, out var value))
                request.Headers.TryAddWithoutValidation(headerName, value.ToArray());
        }
    }
}
