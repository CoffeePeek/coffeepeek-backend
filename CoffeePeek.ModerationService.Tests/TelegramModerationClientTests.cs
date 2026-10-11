using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public class TelegramModerationClientTests
{
    [Fact]
    public async Task SendAsync_PostsChatAndSubmissionDetails()
    {
        var handler = new ReplyHandler(HttpStatusCode.OK, "{\"ok\":true,\"result\":{}} ");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = new TelegramModerationClient(http,
            Options.Create(new TelegramModerationOptions { BotToken = "test-token", ChatId = "-123" }));
        var id = Guid.NewGuid();

        await client.SendAsync(new ModerationSubmissionCreated(ModerationSubmissionKind.Shop, id, null,
            "  Test\n Cafe ", Details: "Main street"), TestContext.Current.CancellationToken);

        handler.Path.Should().Be("/bottest-token/sendMessage");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("chat_id").GetString().Should().Be("-123");
        var message = body.RootElement.GetProperty("text").GetString();
        message.Should().Contain(id.ToString()).And.Contain("Test Cafe").And.Contain("Main street");
        message.Should().NotContain("Test\n Cafe");
    }

    [Fact]
    public async Task SendAsync_UnsuccessfulTelegramReplyThrowsForWolverineRetry()
    {
        var handler = new ReplyHandler(HttpStatusCode.OK, "{\"ok\":false,\"description\":\"rejected\"}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };
        var client = new TelegramModerationClient(http,
            Options.Create(new TelegramModerationOptions { BotToken = "test-token", ChatId = "123" }));

        var send = () => client.SendAsync(new ModerationSubmissionCreated(ModerationSubmissionKind.CheckIn,
            Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class ReplyHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri?.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(content) };
        }
    }
}
