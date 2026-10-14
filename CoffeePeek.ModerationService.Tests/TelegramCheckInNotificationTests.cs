using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Moderation.Infrastructure.Notifications;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public class TelegramCheckInNotificationTests
{
    [Fact]
    public async Task PendingCheckIn_SendsTextLinksButtonsAndPrivatePhoto()
    {
        var userId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var photo = PhotoMetadata.CreateCheckInPhoto(photoId, 0, "latte.png", "image/png", "private/latte.png", 3, userId);
        var review = ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, userId, Guid.NewGuid(),
            "alice", "Вкусный латте", null, null, null, null, DateTime.UtcNow, [photo]);
        var repository = new StubModerationReviewRepository(review);
        var telegramHandler = new CapturingHandler();
        var mediaHandler = new CapturingHandler();
        using var telegramHttp = new HttpClient(telegramHandler) { BaseAddress = new Uri("https://api.telegram.org/") };
        using var mediaHttp = new HttpClient(mediaHandler) { BaseAddress = new Uri("http://media/") };
        var client = new TelegramModerationClient(telegramHttp, Options.Create(new TelegramModerationOptions
        {
            BotToken = "test-token", ChatId = "123", RelayUrl = "https://relay.test/telegram/sendMessage",
            ModeratorUserId = Guid.NewGuid()
        }), repository, new TelegramCheckInPhotoClient(mediaHttp,
            Options.Create(new GatewayAuthOptions { SecretKey = "gateway-test" })));

        await client.SendAsync(new ModerationSubmissionCreated(ModerationSubmissionKind.CheckIn, review.Id,
            userId, "alice", review.ShopId, review.Comment), TestContext.Current.CancellationToken);

        Assert.Equal(2, telegramHandler.Requests.Count);
        using var message = JsonDocument.Parse(telegramHandler.Requests[0].Body);
        var body = message.RootElement;
        Assert.Contains("Вкусный латте", body.GetProperty("text").GetString());
        Assert.Contains($"/api/v1/moderation/check-ins/{review.Id}", body.GetProperty("text").GetString());
        Assert.Contains($"/api/admin/users/{userId}/profile", body.GetProperty("text").GetString());
        var buttons = body.GetProperty("reply_markup").GetProperty("inline_keyboard")[0];
        Assert.Equal($"a:{review.Id:N}", buttons[0].GetProperty("callback_data").GetString());
        Assert.Equal($"r:{review.Id:N}", buttons[1].GetProperty("callback_data").GetString());
        Assert.EndsWith("/telegram/sendPhoto", telegramHandler.Requests[1].Url);
        Assert.Contains("latte.png", telegramHandler.Requests[1].Body);
        Assert.Single(mediaHandler.Requests);
        Assert.EndsWith($"/internal/check-in-photos/{review.CheckInId}/{photoId}", mediaHandler.Requests[0].Url);
        Assert.Equal("gateway-test", mediaHandler.Requests[0].GatewayAuth);
        Assert.Equal(userId.ToString(), mediaHandler.Requests[0].UserId);
    }

    [Fact]
    public async Task AutoApprovedEmptyCheckIn_HasNoDecisionButtons()
    {
        var userId = Guid.NewGuid();
        var review = ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, userId, Guid.NewGuid(),
            "alice", "", null, null, null, null, DateTime.UtcNow, []);
        var repository = new StubModerationReviewRepository(review);
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = new TelegramModerationClient(http, Options.Create(new TelegramModerationOptions
        {
            BotToken = "test-token", ChatId = "123", ModeratorUserId = Guid.NewGuid()
        }), repository, new TelegramCheckInPhotoClient(new HttpClient(),
            Options.Create(new GatewayAuthOptions())));

        await client.SendAsync(new ModerationSubmissionCreated(ModerationSubmissionKind.CheckIn, review.Id, userId),
            TestContext.Current.CancellationToken);

        using var message = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(message.RootElement.TryGetProperty("reply_markup", out _));
        Assert.Contains("Текст: нет", message.RootElement.GetProperty("text").GetString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string Url, string Body, string? GatewayAuth, string? UserId)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Content is null
                    ? "" : await request.Content.ReadAsStringAsync(ct),
                request.Headers.TryGetValues("X-Gateway-Auth", out var auth) ? auth.Single() : null,
                request.Headers.TryGetValues("X-User-Id", out var user) ? user.Single() : null));
            return request.RequestUri.Host == "media"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }
}
