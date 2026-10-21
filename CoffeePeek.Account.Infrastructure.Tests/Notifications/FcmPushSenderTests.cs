using System.Net;
using System.Text;
using System.Text.Json;
using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Account.Infrastructure.Notifications;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Notifications;

public sealed class FcmPushSenderTests
{
    [Theory]
    [InlineData(200, null, "Accepted")]
    [InlineData(404, "UNREGISTERED", "Unregistered")]
    [InlineData(404, null, "Rejected")]
    [InlineData(400, "INVALID_ARGUMENT", "Rejected")]
    [InlineData(401, null, "Retry")]
    [InlineData(403, "THIRD_PARTY_AUTH_ERROR", "Retry")]
    [InlineData(429, "QUOTA_EXCEEDED", "Retry")]
    [InlineData(503, "UNAVAILABLE", "Retry")]
    public async Task Sender_UsesHttpV1AndOnlyRemovesProvenDeadTokens(int status, string? errorCode, string expected)
    {
        var handler = new StubHandler((HttpStatusCode)status, errorCode);
        using var http = new HttpClient(handler);
        var sender = new FcmPushSender(http, GoogleCredential.FromAccessToken("test-oauth-token"),
            Microsoft.Extensions.Options.Options.Create(new PushNotificationOptions { ProjectId = "coffeepeek" }), TimeProvider.System);
        var notification = new PushNotification
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Title = "Title", Body = "Body",
            EntityType = "CheckIn", EntityId = Guid.NewGuid(), Status = "Approved", ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        };
        var result = await sender.SendAsync(new(Guid.NewGuid(), Guid.NewGuid(), "device-token", notification), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
        if (expected == "Retry") Assert.Equal(TimeSpan.FromMinutes(10), result.RetryAfter);
        Assert.Equal("https://fcm.googleapis.com/v1/projects/coffeepeek/messages:send", handler.Url);
        Assert.Equal("Bearer test-oauth-token", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Payload!);
        var message = json.RootElement.GetProperty("message");
        Assert.Equal("device-token", message.GetProperty("token").GetString());
        Assert.Equal("CheckInModeration", message.GetProperty("data").GetProperty("type").GetString());
        Assert.Equal(notification.Id.ToString(), message.GetProperty("data").GetProperty("notificationId").GetString());
        Assert.Equal("alert", message.GetProperty("apns").GetProperty("headers").GetProperty("apns-push-type").GetString());
        Assert.Equal(notification.Id.ToString(), message.GetProperty("android").GetProperty("notification").GetProperty("tag").GetString());
    }

    private sealed class StubHandler(HttpStatusCode status, string? errorCode) : HttpMessageHandler
    {
        public string? Payload { get; private set; }
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Payload = await request.Content!.ReadAsStringAsync(ct);
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    error = new { details = errorCode is null ? [] : new[] { new Dictionary<string, string>
                    {
                        ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError", ["errorCode"] = errorCode
                    } } }
                }), Encoding.UTF8, "application/json")
            };
            response.Headers.RetryAfter = new(System.TimeSpan.FromMinutes(10));
            return response;
        }
    }
}
