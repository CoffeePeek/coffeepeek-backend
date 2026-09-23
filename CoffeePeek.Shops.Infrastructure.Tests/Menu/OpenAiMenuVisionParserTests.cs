using System.Net;
using System.Text;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Infrastructure.Menu;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Menu;

public class OpenAiMenuVisionParserTests
{
    [Fact]
    public async Task ParseAsync_MissingApiKey_FailsWithoutCallingOpenAi()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var parser = CreateParser(factory.Object, new OpenAiOptions { ApiKey = "" });

        var result = await parser.ParseAsync([new MenuVisionPhoto([1], "image/jpeg")]);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("API key");
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ParseAsync_MapsStructuredDrinkJson()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            const string body = """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "{\"drinks\":[{\"rawName\":\" Cappuccino \",\"price\":6.5,\"volumeMl\":250,\"confidence\":0.91}]}"
                      }
                    }
                  ]
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("openai")).Returns(() => new HttpClient(handler));

        var parser = CreateParser(factory.Object, new OpenAiOptions
        {
            ApiKey = "sk-test",
            Model = "gpt-4.1-mini",
            BaseUrl = "https://api.openai.com/v1"
        });

        var result = await parser.ParseAsync([new MenuVisionPhoto([1, 2, 3], "image/jpeg")]);

        result.Success.Should().BeTrue();
        result.Drinks.Should().ContainSingle();
        result.Drinks[0].RawName.Should().Be("Cappuccino");
        result.Drinks[0].Price.Should().Be(6.5m);
        result.Drinks[0].VolumeMl.Should().Be(250);
        captured.Should().NotBeNull();
        captured!.RequestUri!.AbsoluteUri.Should().Be("https://api.openai.com/v1/chat/completions");
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("sk-test");
    }

    private static OpenAiMenuVisionParser CreateParser(IHttpClientFactory factory, OpenAiOptions options) =>
        new(factory, Options.Create(options), NullLogger<OpenAiMenuVisionParser>.Instance);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
