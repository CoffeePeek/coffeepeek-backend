using System.Net;
using System.Text;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Infrastructure.Media;
using CoffeePeek.ShopsService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.ShopsService.Tests;

public class CheckInPhotoAccessTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid CheckInId = Guid.NewGuid();
    private static readonly Guid PhotoId = Guid.NewGuid();
    private static UnifiedCheckInDto Visible => new() { Id = CheckInId, UserId = Author,
        Photos = [new(PhotoId, "photo.jpg", "image/jpeg", "private-key", 5, 0, "protected-url")] };

    private static (CheckInPhotosController Controller, FakeQueries Queries, FakeMedia Handler) Setup(Guid? reader)
    {
        var queries = new FakeQueries(); var handler = new FakeMedia();
        var media = new MediaCheckInPhotoClient(new FakeClients(handler), Options.Create(new GatewayAuthOptions { SecretKey = "shared-secret" }));
        var controller = new CheckInPhotosController(queries, media, new FakeUser(reader)) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Response.Body = new MemoryStream();
        return (controller, queries, handler);
    }

    [Fact]
    public async Task EveryDownload_RechecksCurrentVisibilityBeforeReadingBytes()
    {
        var (controller, queries, media) = Setup(Author); queries.Detail = Visible;
        (await controller.Download(CheckInId, PhotoId, TestContext.Current.CancellationToken)).Should().BeOfType<EmptyResult>();
        queries.Detail = null; // The same route is denied after privacy change or deletion.
        (await controller.Download(CheckInId, PhotoId, TestContext.Current.CancellationToken)).Should().BeOfType<NotFoundResult>();
        queries.Readers.Should().Equal(Author, Author);
        media.Calls.Should().Be(1);
        controller.Response.Headers.CacheControl.ToString().Should().Be("private, no-store");
    }

    [Fact]
    public async Task EligibleAnonymousDownload_UsesTheCanonicalAuthorAndNeverRedirects()
    {
        var (controller, queries, media) = Setup(null); queries.Detail = Visible;
        (await controller.Download(CheckInId, PhotoId, TestContext.Current.CancellationToken)).Should().BeOfType<EmptyResult>();
        queries.Readers.Should().Equal((Guid?)null);
        media.OwnerHeader.Should().Be(Author.ToString());
        media.SecretHeader.Should().Be("shared-secret");
        media.Path.Should().Be($"/internal/check-in-photos/{CheckInId}/{PhotoId}");
        Encoding.UTF8.GetString(((MemoryStream)controller.Response.Body).ToArray()).Should().Be("bytes");
        controller.Response.Headers.Location.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownPhotoOrInaccessibleVisit_DoesNotContactMedia()
    {
        var (controller, queries, media) = Setup(Guid.NewGuid());
        (await controller.Download(CheckInId, PhotoId, TestContext.Current.CancellationToken)).Should().BeOfType<NotFoundResult>();
        queries.Detail = Visible;
        (await controller.Download(CheckInId, Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeOfType<NotFoundResult>();
        media.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ModeratorDownload_UsesTheRequestedCurrentRevisionAndDeniesStaleSnapshot()
    {
        var (controller, queries, media) = Setup(Guid.NewGuid()); queries.Snapshot = Visible;
        (await controller.DownloadModeration(CheckInId, PhotoId, 3, TestContext.Current.CancellationToken)).Should().BeOfType<EmptyResult>();
        queries.Snapshot = null;
        (await controller.DownloadModeration(CheckInId, PhotoId, 2, TestContext.Current.CancellationToken)).Should().BeOfType<NotFoundResult>();
        queries.Revisions.Should().Equal(3, 2); media.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false,"text/html","text/html")]
    [InlineData(true,"text/html","text/html")]
    [InlineData(false,"image/svg+xml","image/svg+xml")]
    [InlineData(true,"text/html","image/jpeg")]
    [InlineData(false,"image/jpeg","text/html")]
    public async Task UnsafeHistoricalMime_IsAnAttachmentInAuthorAndModeratorResponses(
        bool moderator,string storedType,string upstreamType)
    {
        var (controller,queries,media)=Setup(Author);
        var photo=Visible.Photos.Single() with { ContentType=storedType };
        queries.Detail=Visible with { Photos=[photo] }; queries.Snapshot=queries.Detail;
        media.ContentType=upstreamType;
        var result=moderator
            ? await controller.DownloadModeration(CheckInId,PhotoId,1,TestContext.Current.CancellationToken)
            : await controller.Download(CheckInId,PhotoId,TestContext.Current.CancellationToken);
        result.Should().BeOfType<EmptyResult>();
        controller.Response.ContentType.Should().Be("application/octet-stream");
        controller.Response.Headers.ContentDisposition.ToString().Should().Be("attachment");
        controller.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    private sealed class FakeMedia : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? OwnerHeader { get; private set; }
        public string? SecretHeader { get; private set; }
        public string? Path { get; private set; }
        public string ContentType { get; set; }="image/jpeg";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; OwnerHeader = request.Headers.GetValues(GatewayHeaderConsts.XUserId).Single();
            SecretHeader = request.Headers.GetValues(GatewayHeaderConsts.XGatewayAuth).Single();
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("bytes", Encoding.UTF8, ContentType) });
        }
    }
    private sealed class FakeClients(FakeMedia media) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(media, false) { BaseAddress = new Uri("http://media") };
    }
    private sealed class FakeUser(Guid? id) : IUserContext
    {
        public Guid? GetUserId() => id;
        public Guid GetUserIdOrThrow() => id ?? throw new InvalidOperationException();
        public string? GetUsername() => null;
        public string GetUsernameOrThrow() => throw new NotSupportedException();
        public string? GetUserRole() => null;
        public bool IsAuthenticated => id.HasValue;
    }
    private sealed class FakeQueries : ICheckInQueries
    {
        public UnifiedCheckInDto? Detail { get; set; }
        public UnifiedCheckInDto? Snapshot { get; set; }
        public List<Guid?> Readers { get; } = [];
        public List<int> Revisions { get; } = [];
        public Task<UnifiedCheckInDto?> GetUnifiedByIdAsync(Guid id, Guid? readerId, CancellationToken ct = default) { Readers.Add(readerId); return Task.FromResult(Detail); }
        public Task<UnifiedCheckInDto?> GetModerationSnapshotAsync(Guid id, int revision, CancellationToken ct = default) { Revisions.Add(revision); return Task.FromResult(Snapshot); }
        public Task<CheckInPage> GetUnifiedByUserAsync(Guid userId, int pageNumber, int pageSize, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PublicCheckInPage> GetPublicAsync(Guid? readerId, Guid? cityId, Guid? shopId, Guid? authorId, int pageSize, string? cursor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<(decimal AverageRating, int Count)> GetStatsByShopAsync(Guid shopId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UnifiedCheckInDto[]> GetByUserAndShopAsync(Guid userId, Guid shopId, int page = 1, int pageSize = 10, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
