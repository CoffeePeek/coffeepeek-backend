using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Infrastructure.Notifications;
using CoffeePeek.Shared.Auth.Options;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public class TelegramModerationClientTests
{
    [Theory]
    [InlineData("123456:fake-test-token")]
    [InlineData("test-token")]
    public async Task SendAsync_PostsChatAndSubmissionDetails(string token)
    {
        var handler = new ReplyHandler(HttpStatusCode.OK, "{\"ok\":true,\"result\":{}} ");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = CreateClient(http, new TelegramModerationOptions { BotToken = token, ChatId = "-123" });
        var id = Guid.NewGuid();

        await client.SendAsync(new ModerationSubmissionCreated(ModerationSubmissionKind.Shop, id, null,
            "  Test\n Cafe ", Details: "Main street"), TestContext.Current.CancellationToken);

        handler.Url.Should().Be($"https://api.telegram.org/bot{token}/sendMessage");
        handler.Authorization.Should().BeNull();
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("chat_id").GetString().Should().Be("-123");
        var message = body.RootElement.GetProperty("text").GetString();
        message.Should().Contain(id.ToString()).And.Contain("Test Cafe").And.Contain("Main street");
        message.Should().NotContain("Test\n Cafe");
    }

    [Fact]
    public async Task SendAsync_RelayReceivesTokenOnlyInAuthorizationHeader()
    {
        const string token = "123456:fake-test-token";
        const string relayUrl = "https://relay.example/telegram/sendMessage";
        var handler = new ReplyHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = CreateClient(http, new TelegramModerationOptions
        {
            BotToken = token, ChatId = "-123", RelayUrl = relayUrl
        });
        var id = Guid.NewGuid();

        await client.SendAsync(new ModerationSubmissionCreated(ModerationSubmissionKind.Shop, id, null),
            TestContext.Current.CancellationToken);

        handler.Url.Should().Be(relayUrl).And.NotContain(token);
        handler.Authorization.Should().Be($"Bearer {token}");
        handler.Method.Should().Be(HttpMethod.Post);
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("chat_id").GetString().Should().Be("-123");
        body.RootElement.GetProperty("text").GetString().Should().Contain(id.ToString());
        handler.Body.Should().NotContain(token);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"ok\":false,\"description\":\"rejected\"}", false)]
    [InlineData(HttpStatusCode.Forbidden, "{\"ok\":false}", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"ok\":false}", false)]
    [InlineData(HttpStatusCode.BadGateway, "unavailable", false)]
    [InlineData(HttpStatusCode.OK, "{\"ok\":false}", true)]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", true)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"ok\":false}", true)]
    [InlineData(HttpStatusCode.BadGateway, "unavailable", true)]
    [InlineData(HttpStatusCode.TemporaryRedirect, "redirect", true)]
    public async Task SendAsync_UnsuccessfulTelegramReplyThrowsForWolverineRetry(HttpStatusCode status, string response,
        bool useRelay)
    {
        var handler = new ReplyHandler(status, response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = CreateClient(http, new TelegramModerationOptions
            {
                BotToken = "test-token", ChatId = "123",
                RelayUrl = useRelay ? "https://relay.example/telegram/sendMessage" : ""
            });

        var send = () => client.SendAsync(new ModerationSubmissionCreated(ModerationSubmissionKind.Shop,
            Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        var error = await send.Should().ThrowAsync<HttpRequestException>();
        error.Which.ToString().Should().NotContain("test-token");
    }

    private static TelegramModerationClient CreateClient(HttpClient http, TelegramModerationOptions settings) =>
        new(http, Options.Create(settings), new StubModerationReviewRepository(),
            new TelegramCheckInPhotoClient(new HttpClient(), Options.Create(new GatewayAuthOptions())));

    private sealed class ReplyHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }
        public HttpMethod? Method { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri?.AbsoluteUri;
            Authorization = request.Headers.Authorization?.ToString();
            Method = request.Method;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(content) };
        }
    }
}
