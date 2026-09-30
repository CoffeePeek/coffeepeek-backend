using System.Net;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Account;

public sealed class PublicAddressHttpClientTests
{
    [Fact]
    public async Task Lookup_ValidatesInputsBatchesIdsAndFailsClosed()
    {
        var requests = new List<HttpRequestMessage>();
        var status = HttpStatusCode.OK;
        using var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            request.Headers.GetValues(GatewayHeaderConsts.XGatewayAuth).Should().Equal("internal-test-secret");
            return new HttpResponseMessage(status) { Content = new StringContent("[]") };
        })) { BaseAddress = new Uri("http://localhost") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("shops")).Returns(client);
        var lookup = new PublicAddressHttpClient(factory.Object, Options.Create(new GatewayAuthOptions { SecretKey = "internal-test-secret" }));
        var ct = TestContext.Current.CancellationToken;

        foreach (var invalid in new string?[] { null, "", " ", new('a', 101) })
        {
            var act = () => lookup.ResolveIdsAsync("shops", "beans", [invalid!], ct);
            await act.Should().ThrowAsync<ValidationException>();
        }
        requests.Should().BeEmpty();
        var ids = Enumerable.Range(0, 205).Select(_ => Guid.NewGuid()).ToArray();
        (await lookup.GetByIdsAsync("shops", "beans", [.. ids, ids[0], Guid.Empty], ct)).Should().BeEmpty();
        requests.Should().HaveCount(3);
        requests.Select(x => x.RequestUri!.Query.Split('&').Length).Should().Equal(100, 100, 5);

        status = HttpStatusCode.NotFound;
        (await lookup.ResolveAsync("shops", "beans", "missing", ct)).Should().BeNull();
        var resolveMissing = () => lookup.ResolveIdsAsync("shops", "beans", ["missing"], ct);
        await resolveMissing.Should().ThrowAsync<NotFoundException>();
        status = HttpStatusCode.BadRequest;
        var invalidAddress = () => lookup.ResolveAsync("shops", "beans", "invalid", ct);
        await invalidAddress.Should().ThrowAsync<ValidationException>();
        status = HttpStatusCode.Unauthorized;
        var unavailable = () => lookup.GetByIdsAsync("shops", "beans", [ids[0]], ct);
        await unavailable.Should().ThrowAsync<ServiceUnavailableException>();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request));
    }
}
