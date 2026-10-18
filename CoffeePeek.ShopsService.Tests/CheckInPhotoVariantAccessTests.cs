using System.Net;
using System.Net.Http.Headers;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.ShopsService.Tests;

public sealed class CheckInPhotoVariantAccessTests
{
    private static readonly string Source = new('a', 64);
    private static readonly CheckInPhotoAccess Access = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "image/jpeg", CheckInKind.Home, 1)
        { IsPubliclyVisible = true, SourceVersion = Source };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConditionalRequestRechecksAccessAndCannotReturn304AfterHideOrBlock()
    {
        var fixture = new Fixture();
        var first = fixture.Controller();
        Assert.IsType<EmptyResult>(await Download(first));
        Assert.Equal("private, max-age=0, must-revalidate", first.Response.Headers.CacheControl);
        Assert.Equal(3, first.Response.ContentLength);
        Assert.Equal("image/webp", first.Response.ContentType);
        Assert.Equal("nosniff", first.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("Authorization, Cookie", first.Response.Headers.Vary);
        fixture.Media.Status = HttpStatusCode.NotModified;
        var second = fixture.Controller(); second.Request.Headers.IfNoneMatch = "W/\"digest\"";
        Assert.Equal(304, Assert.IsType<StatusCodeResult>(await Download(second)).StatusCode);
        Assert.Equal(0, second.Response.Body.Length);
        fixture.Visible = null;
        var blocked = fixture.Controller(); blocked.Request.Headers.IfNoneMatch = "*";
        Assert.IsType<NotFoundResult>(await Download(blocked));
        Assert.Equal("private, no-store", blocked.Response.Headers.CacheControl);
        Assert.Equal(2, fixture.Media.Calls);
        fixture.Queries.Verify(q => q.GetAsync(Access.CheckInId, Access.PhotoId, Access.OwnerId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task OwnerOnlyPhotoIgnoresConditionalAndReturnsNoStoreBytes()
    {
        var fixture = new Fixture { Visible = Access with { IsPubliclyVisible = false } };
        var controller = fixture.Controller(); controller.Request.Headers.IfNoneMatch = "*";
        Assert.IsType<EmptyResult>(await Download(controller));
        Assert.Null(fixture.Media.Conditional);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        fixture.Media.Status = HttpStatusCode.NotModified;
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await Download(fixture.Controller())).StatusCode);
    }

    [Fact]
    public async Task HeadForwardsHeadAndHasTheSameRepresentationHeadersWithoutBody()
    {
        var fixture = new Fixture(); var controller = fixture.Controller(); controller.Request.Method = "HEAD";
        Assert.IsType<EmptyResult>(await Download(controller));
        Assert.Equal(HttpMethod.Head, fixture.Media.Method);
        Assert.Equal(3, controller.Response.ContentLength);
        Assert.Equal("\"digest\"", controller.Response.Headers.ETag);
        Assert.Equal(0, controller.Response.Body.Length);
        Assert.Equal(Access.OwnerId.ToString(), fixture.Media.Owner);
    }

    [Fact]
    public async Task StaleBindingAndDisabledRolloutNeverContactMedia()
    {
        var fixture = new Fixture { Visible = Access with { SourceVersion = new string('b', 64) } };
        Assert.IsType<NotFoundResult>(await Download(fixture.Controller()));
        fixture.Visible = Access;
        Assert.IsType<NotFoundResult>(await Download(fixture.Controller(enabled: false)));
        Assert.Equal(0, fixture.Media.Calls);
    }

    [Fact]
    public async Task UncachedAccountFailureNeverReusesAnEtagOrContactsMedia()
    {
        var fixture = new Fixture();
        fixture.Queries.Setup(q => q.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceUnavailableException("Account unavailable"));
        var controller = fixture.Controller(); controller.Request.Headers.IfNoneMatch = "*";
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => Download(controller));
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Equal(0, fixture.Media.Calls);
    }

    [Fact]
    public async Task SlowAccessDecisionCannotResetRevocationDeadline()
    {
        var fixture = new Fixture();
        fixture.Queries.Setup(q => q.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { fixture.Clock.Advance(41); return Access; });
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await Download(fixture.Controller())).StatusCode);
        Assert.Equal(0, fixture.Media.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.Forbidden, 503)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]
    public async Task UpstreamErrorsAreNeverCacheable(HttpStatusCode status, int expected)
    {
        var fixture = new Fixture(); fixture.Media.Status = status;
        var controller = fixture.Controller();
        Assert.Equal(expected, Assert.IsAssignableFrom<StatusCodeResult>(await Download(controller)).StatusCode);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Empty(controller.Response.Headers.ETag.ToString());
    }

    private static Task<IActionResult> Download(CheckInPhotoVariantsController controller) =>
        controller.Download(Access.CheckInId, Access.PhotoId, Source, 1, "feed", "webp", Ct);

    private sealed class Fixture
    {
        public CheckInPhotoAccess? Visible = Access;
        public readonly Mock<ICheckInPhotoAccessQueries> Queries = new();
        public readonly MediaHandler Media = new();
        public readonly Clock Clock = new();
        public Fixture() => Queries.Setup(q => q.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Visible);
        public CheckInPhotoVariantsController Controller(bool enabled = true)
        {
            var user = new Mock<IUserContext>(); user.Setup(u => u.GetUserId()).Returns(Access.OwnerId);
            var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() => new HttpClient(Media, false) { BaseAddress = new Uri("http://media") });
            var controller = new CheckInPhotoVariantsController(Queries.Object,
                new MediaCheckInPhotoClient(factory.Object, Options.Create(new GatewayAuthOptions { SecretKey = "test" })),
                user.Object, Options.Create(new CheckInPhotoDeliveryOptions { ServeVariants = enabled }), Clock)
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            controller.Request.Method = "GET"; controller.Response.Body = new MemoryStream();
            return controller;
        }
    }
    private sealed class Clock : TimeProvider
    {
        private double seconds;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => (long)(seconds * TimeSpan.TicksPerSecond);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(seconds);
        public void Advance(double value) => seconds += value;
    }
    private sealed class MediaHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public int Calls;
        public string? Conditional;
        public string? Owner;
        public HttpMethod? Method;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Method = request.Method;
            Conditional = request.Headers.TryGetValues("If-None-Match", out var values) ? values.Single() : null;
            Owner = request.Headers.GetValues("X-User-Id").Single();
            Assert.DoesNotContain("X-User-Role", request.Headers.Select(h => h.Key));
            Assert.Null(request.Headers.Range);
            var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentLength = 3;
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/webp");
            response.Headers.ETag = new EntityTagHeaderValue("\"digest\"");
            return Task.FromResult(response);
        }
    }
}
