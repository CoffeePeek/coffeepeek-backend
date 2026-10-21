using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Account.Application.Features.Notifications;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Notifications;

public sealed class FcmPushSender(HttpClient http, GoogleCredential credential, IOptions<PushNotificationOptions> options,
    TimeProvider time) : IPushSender
{
    public async Task<PushSendResult> SendAsync(PushDispatch dispatch, CancellationToken ct)
    {
        var n = dispatch.Notification;
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = n.Id.ToString(), ["category"] = n.Category.ToString(), ["type"] = n.Type.ToString(),
            ["userId"] = n.UserId.ToString()
        };
        if (n.EntityType is not null) data["entityType"] = n.EntityType;
        if (n.EntityId is { } id) data["entityId"] = id.ToString();
        if (n.Status is not null) data["status"] = n.Status;
        var remaining = (int)Math.Clamp((n.ExpiresAtUtc - time.GetUtcNow().UtcDateTime).TotalSeconds, 0, 2419200);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(options.Value.ProjectId)}/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct));
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                token = dispatch.Token, notification = new { title = n.Title, body = n.Body }, data,
                android = new { ttl = $"{remaining}s", notification = new { tag = n.Id.ToString() } },
                apns = new
                {
                    headers = new Dictionary<string, string>
                    {
                        ["apns-push-type"] = "alert", ["apns-priority"] = "10",
                        ["apns-collapse-id"] = n.Id.ToString(),
                        ["apns-expiration"] = new DateTimeOffset(n.ExpiresAtUtc).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
                    },
                    payload = new { aps = new { sound = "default" } }
                }
            }
        });
        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return PushSendResult.Accepted;
        string? code = null;
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (json.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                foreach (var detail in details.EnumerateArray())
                    if (detail.TryGetProperty("@type", out var type)
                        && type.GetString() == "type.googleapis.com/google.firebase.fcm.v1.FcmError"
                        && detail.TryGetProperty("errorCode", out var errorCode)) code = errorCode.GetString();
        }
        catch (JsonException) { }
        if (code == "UNREGISTERED") return PushSendResult.Unregistered;
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
            || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || code == "THIRD_PARTY_AUTH_ERROR")
        {
            var retry = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date - time.GetUtcNow()) ?? TimeSpan.FromMinutes(5);
            return new("Retry", retry);
        }
        // INVALID_ARGUMENT can describe the payload; only UNREGISTERED proves the device token is dead.
        return new("Rejected");
    }
}
