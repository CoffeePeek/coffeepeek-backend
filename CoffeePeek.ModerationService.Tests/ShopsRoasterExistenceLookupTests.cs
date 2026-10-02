using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Moderation.Infrastructure.Shops;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public sealed class ShopsRoasterExistenceLookupTests
{
    private const string Secret = "test-internal-secret-at-least-32-characters";

    [Fact]
    public async Task ExistsByNameAsync_UsesAuthenticatedTargetedQuery()
    {
        var handler = new RecordingHandler();
        var lookup = new ShopsRoasterExistenceLookup(
            new ClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://shops") }),
            Options.Create(new GatewayAuthOptions { SecretKey = Secret }),
            NullLogger<ShopsRoasterExistenceLookup>.Instance);

        var exists = await lookup.ExistsByNameAsync(" Roast%_House ", TestContext.Current.CancellationToken);

        exists.Should().BeTrue();
        handler.Path.Should().Be("/internal/roasters/exists?name=Roast%25_House");
        handler.Secret.Should().Be(Secret);
        handler.Calls.Should().Be(1);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            name.Should().Be("shops-roaster-lookup");
            return client;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Secret { get; private set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri?.PathAndQuery;
            Secret = request.Headers.GetValues(GatewayHeaderConsts.XGatewayAuth).Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(true) });
        }
    }
}
