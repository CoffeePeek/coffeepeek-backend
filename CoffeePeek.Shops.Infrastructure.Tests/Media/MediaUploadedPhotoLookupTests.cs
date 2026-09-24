using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Infrastructure.Media;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Media;

public class MediaUploadedPhotoLookupTests
{
    private const string Secret = "test-gateway-secret-at-least-32-characters";

    private static MediaUploadedPhotoLookup CreateLookup(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(MediaUploadedPhotoLookup.HttpClientName))
            .Returns(() => new HttpClient(handler) { BaseAddress = new Uri("http://coffeepeek-mediaservice") });

        return new MediaUploadedPhotoLookup(
            factory.Object,
            Options.Create(new GatewayAuthOptions { SecretKey = Secret }),
            NullLogger<MediaUploadedPhotoLookup>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_PostsToResolveWithGatewayAndUserHeaders_AndReturnsPhotos()
    {
        var userId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        string? body = null;
        var lookup = CreateLookup(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Response<List<ResolvedUploadedPhotoDto>>.Success(
                    [new ResolvedUploadedPhotoDto("reviews/a.jpg", "a.jpg", "image/jpeg", 42)]))
            };
        });

        var result = await lookup.ResolveAsync(userId, ["reviews/a.jpg"], TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Photos.Should().ContainSingle()
            .Which.Should().Be(new ResolvedUploadedPhotoDto("reviews/a.jpg", "a.jpg", "image/jpeg", 42));

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.AbsolutePath.Should().Be("/api/Photos/resolve");
        captured.Headers.GetValues(GatewayHeaderConsts.XGatewayAuth).Should().ContainSingle().Which.Should().Be(Secret);
        captured.Headers.GetValues(GatewayHeaderConsts.XUserId).Should().ContainSingle().Which.Should().Be(userId.ToString());

        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("storageKeys").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("reviews/a.jpg");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ResolveAsync_NonSuccessStatus_Fails(HttpStatusCode status)
    {
        var lookup = CreateLookup(_ => Task.FromResult(new HttpResponseMessage(status)));

        var result = await lookup.ResolveAsync(Guid.NewGuid(), ["reviews/a.jpg"], TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Photos.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ErrorPayload_Fails()
    {
        var lookup = CreateLookup(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Response<List<ResolvedUploadedPhotoDto>>.Error("too many keys"))
        }));

        var result = await lookup.ResolveAsync(Guid.NewGuid(), ["reviews/a.jpg"], TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_HttpRequestException_Fails()
    {
        var lookup = CreateLookup(_ => throw new HttpRequestException("connection refused"));

        var result = await lookup.ResolveAsync(Guid.NewGuid(), ["reviews/a.jpg"], TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_Timeout_Fails()
    {
        var lookup = CreateLookup(_ => throw new TaskCanceledException("timeout"));

        var result = await lookup.ResolveAsync(Guid.NewGuid(), ["reviews/a.jpg"], TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
