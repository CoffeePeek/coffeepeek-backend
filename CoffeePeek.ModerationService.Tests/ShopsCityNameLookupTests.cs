using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos.Internal;
using CoffeePeek.Moderation.Infrastructure.Shops;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public class ShopsCityNameLookupTests
{
    [Theory]
    [InlineData("found")]
    [InlineData("missing")]
    [InlineData("unavailable")]
    [InlineData("wrong_city")]
    public async Task Lookup_UsesTrustedCityIdAndNeverSubstitutesAnotherCity(string outcome)
    {
        var id = Guid.NewGuid();
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal($"/internal/cities/{id:D}", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-gateway-secret", Assert.Single(request.Headers.GetValues(GatewayHeaderConsts.XGatewayAuth)));
            return new HttpResponseMessage(outcome switch
            {
                "missing" => HttpStatusCode.NotFound,
                "unavailable" => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.OK
            }) { Content = JsonContent.Create(new CityDto { Id = outcome == "wrong_city" ? Guid.NewGuid() : id, Name = " Брест " }) };
        })) { BaseAddress = new Uri("http://shops.test") };
        var lookup = new ShopsCityNameLookup(new ClientFactory(client), Options.Create(new GatewayAuthOptions
        {
            SecretKey = "test-gateway-secret"
        }));

        if (outcome is "wrong_city" or "unavailable")
            await Assert.ThrowsAsync<HttpRequestException>(() => lookup.GetNameAsync(id, TestContext.Current.CancellationToken));
        else
            Assert.Equal(outcome == "missing" ? null : "Брест", await lookup.GetNameAsync(id, TestContext.Current.CancellationToken));
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("shops-roaster-lookup", name);
            return client;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
