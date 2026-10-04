using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Moderation.Infrastructure.Media;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ModerationService.Tests;

public sealed class MediaReviewPhotoLookupTests
{
    [Fact]
    public async Task Resolve_SendsGatewaySecretAndOwnerWithOnlyRequestedKeys()
    {
        var user = Guid.NewGuid();
        var handler = new StubHandler(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/api/Photos/resolve");
            request.Headers.GetValues("X-Gateway-Auth").Should().Equal("service-secret");
            request.Headers.GetValues("X-User-Id").Should().Equal(user.ToString());
            (await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("key");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Response<List<ResolvedUploadedPhotoDto>>.Success([new("key", "real.jpg", "image/jpeg", 42)]))
            };
        });
        var result = await Lookup(handler).ResolveAsync(user, ["key"], TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeTrue();
        result.Photos.Should().ContainSingle().Which.SizeBytes.Should().Be(42);
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(403, false)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    public async Task Resolve_RejectionVersusUnavailable_IsExplicit(int code, bool transient)
    {
        var result = await Lookup(new StubHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code))))
            .ResolveAsync(Guid.NewGuid(), ["key"], TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeFalse();
        result.IsTransientFailure.Should().Be(transient);
    }

    [Fact]
    public async Task Resolve_TransportFailureOrMalformedPayload_IsUnavailable()
    {
        foreach (var handler in new[]
        {
            new StubHandler(_ => throw new HttpRequestException("offline")),
            new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid json") }))
        })
        {
            var result = await Lookup(handler).ResolveAsync(Guid.NewGuid(), ["key"], TestContext.Current.CancellationToken);
            result.Succeeded.Should().BeFalse();
            result.IsTransientFailure.Should().BeTrue();
        }
    }

    private static MediaReviewPhotoLookup Lookup(HttpMessageHandler handler) => new(
        new Factory(new HttpClient(handler) { BaseAddress = new Uri("http://media") }),
        Options.Create(new GatewayAuthOptions { SecretKey = "service-secret" }), NullLogger<MediaReviewPhotoLookup>.Instance);
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            name.Should().Be(MediaReviewPhotoLookup.HttpClientName);
            return client;
        }
    }
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
