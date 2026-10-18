using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Infrastructure.Account;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Account;

public sealed class AuthorVisibilityLookupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GuestsSkipLookup_AuthenticatedRequestsUseTrustedHeaderAndDoNotCache()
    {
        var viewer = Guid.NewGuid(); var blocked = Guid.NewGuid(); var calls = 0;
        using var handler = new VisibilityHandler(request =>
        {
            calls++;
            Assert.Equal($"/internal/users/{viewer:D}/excluded-authors", request.RequestUri!.AbsolutePath);
            Assert.Equal("visibility-test-secret", request.Headers.GetValues(GatewayHeaderConsts.XGatewayAuth).Single());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(calls == 1 ? new[] { blocked } : Array.Empty<Guid>()) };
        });
        var lookup = Lookup(handler);
        Assert.Empty(await lookup.ExcludedUserIdsAsync(null, Ct)); Assert.Equal(0, calls);
        Assert.Equal(new[] { blocked }, await lookup.ExcludedUserIdsAsync(viewer, Ct));
        Assert.Empty(await lookup.ExcludedUserIdsAsync(viewer, Ct)); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(503, "[]")]
    [InlineData(401, "[]")]
    [InlineData(200, "null")]
    [InlineData(200, "invalid json")]
    public async Task UnavailableOrInvalidPolicyNeverFallsBackToPublicContent(int status, string body)
    {
        using var handler = new VisibilityHandler(_ => new((HttpStatusCode)status) { Content = new StringContent(body) });
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => Lookup(handler).ExcludedUserIdsAsync(Guid.NewGuid(), Ct));
    }

    private static AccountAuthorVisibilityLookup Lookup(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("account-user-lookup")).Returns(() => new HttpClient(handler, false) { BaseAddress = new Uri("http://account") });
        return new(factory.Object, Options.Create(new GatewayAuthOptions { SecretKey = "visibility-test-secret" }));
    }

    private sealed class VisibilityHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
