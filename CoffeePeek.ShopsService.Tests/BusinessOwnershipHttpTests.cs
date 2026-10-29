using System.Net;
using System.Net.Http.Json;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Application.Features.Owner.ReorderPhotos;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.ShopsService.Controllers;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class BusinessOwnershipHttpTests
{
    [Fact]
    public async Task AnonymousOrUntrustedIdentityCannotReadPrivateOwnershipData()
    {
        await using var fixture = await Fixture.Start();
        foreach (var path in new[] { "/api/owner/ownership-claims", "/api/owner/coffee-shops", "/api/admin/ownership-claims" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var forged = fixture.Request(HttpMethod.Get, "/api/owner/ownership-claims", "Admin");
        forged.Headers.Remove(GatewayHeaderConsts.XGatewayAuth);
        using var denied = await fixture.Client.SendAsync(forged, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Empty(fixture.Bus.Invocations);
    }

    [Fact]
    public async Task NormalUserSubmitsAndReadsOnlyTheirOwnClaimsWithTrustedIdentity()
    {
        await using var fixture = await Fixture.Start();
        var claimId = Guid.NewGuid();
        fixture.Bus.Setup(b => b.InvokeAsync<Response<OwnershipClaimDto>>(
                It.IsAny<SubmitOwnershipClaimCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<OwnershipClaimDto>.Success(Claim(claimId)));
        fixture.Bus.Setup(b => b.InvokeAsync<Response<OwnershipClaimPage>>(
                It.IsAny<GetOwnershipClaimsQuery>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<OwnershipClaimPage>.Success(new([], 0, 1, 20)));
        fixture.Bus.Setup(b => b.InvokeAsync<Response<OwnershipClaimDto>>(
                It.IsAny<GetOwnershipClaimByIdQuery>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<OwnershipClaimDto>.Error(HttpStatusCode.NotFound, "Claim not found."));

        using var submit = fixture.Request(HttpMethod.Post, "/api/owner/ownership-claims", "User",
            new { businessType = "CoffeeShop", businessSlug = "test-shop", contact = "owner@example.com", evidence = "Official email", applicantUserId = Guid.NewGuid() });
        using var created = await fixture.Client.SendAsync(submit, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith($"/api/owner/ownership-claims/{claimId}", created.Headers.Location!.ToString());
        Assert.True(created.Headers.CacheControl?.NoStore);
        fixture.Bus.Verify(b => b.InvokeAsync<Response<OwnershipClaimDto>>(
            new SubmitOwnershipClaimCommand(fixture.UserId, BusinessType.CoffeeShop, "test-shop", "owner@example.com", "Official email"),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);

        using var mine = fixture.Request(HttpMethod.Get, $"/api/owner/ownership-claims?applicantUserId={Guid.NewGuid()}", "User");
        using var page = await fixture.Client.SendAsync(mine, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore);
        fixture.Bus.Verify(b => b.InvokeAsync<Response<OwnershipClaimPage>>(
            It.Is<GetOwnershipClaimsQuery>(q => q.ApplicantUserId == fixture.UserId), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);

        using var other = fixture.Request(HttpMethod.Get, $"/api/owner/ownership-claims/{claimId}", "User");
        using var missing = await fixture.Client.SendAsync(other, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        fixture.Bus.Verify(b => b.InvokeAsync<Response<OwnershipClaimDto>>(
            new GetOwnershipClaimByIdQuery(claimId, fixture.UserId), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Theory]
    [InlineData("User", HttpStatusCode.Forbidden)]
    [InlineData("Moderator", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.OK)]
    public async Task OnlyAdminMayReadQueueAuditOrDecide(string role, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.Start();
        var claimId = Guid.NewGuid();
        fixture.Bus.Setup(b => b.InvokeAsync<Response<OwnershipClaimPage>>(
                It.IsAny<GetOwnershipClaimsQuery>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<OwnershipClaimPage>.Success(new([], 0, 1, 20)));
        fixture.Bus.Setup(b => b.InvokeAsync<Response<OwnershipChangePage>>(
                It.IsAny<GetOwnershipChangesQuery>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<OwnershipChangePage>.Success(new([], 0, 1, 20)));
        fixture.Bus.Setup(b => b.InvokeAsync<Response<OwnershipClaimDto>>(
                It.IsAny<ReviewOwnershipClaimCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<OwnershipClaimDto>.Success(Claim(claimId)));

        foreach (var path in new[] { "/api/admin/ownership-claims", "/api/admin/ownership-changes" })
        {
            using var request = fixture.Request(HttpMethod.Get, path, role);
            using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(expected, response.StatusCode);
        }
        foreach (var decision in new[] { "approve", "reject" })
        {
            using var request = fixture.Request(HttpMethod.Post, $"/api/admin/ownership-claims/{claimId}/{decision}", role,
                new { reason = "Verified", actorUserId = Guid.NewGuid() });
            using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(expected, response.StatusCode);
        }
        if (role == "Admin")
        {
            fixture.Bus.Verify(b => b.InvokeAsync<Response<OwnershipClaimDto>>(
                new ReviewOwnershipClaimCommand(claimId, fixture.UserId, true, "Verified"), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
            fixture.Bus.Verify(b => b.InvokeAsync<Response<OwnershipClaimDto>>(
                new ReviewOwnershipClaimCommand(claimId, fixture.UserId, false, "Verified"), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
            fixture.Bus.Verify(b => b.InvokeAsync<Response<OwnershipClaimPage>>(
                It.Is<GetOwnershipClaimsQuery>(q => q.ApplicantUserId == null && q.Status == OwnershipClaimStatus.Pending),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
        }
        else Assert.Empty(fixture.Bus.Invocations);
    }

    [Fact]
    public async Task NormalUserReachesShopOwnerOperationsButResourceFailureRemains404()
    {
        await using var fixture = await Fixture.Start();
        var shopId = Guid.NewGuid();
        fixture.Bus.Setup(b => b.InvokeAsync<Response<GetOwnerCoffeeShopsResponse>>(
                It.IsAny<GetOwnerCoffeeShopsQuery>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<GetOwnerCoffeeShopsResponse>.Success(new([])));
        fixture.Bus.Setup(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                It.IsAny<GetOwnerCoffeeShopByIdQuery>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<AdminPublishedShopDto>.Error(HttpStatusCode.NotFound, "Shop not found."));

        using var mine = fixture.Request(HttpMethod.Get, "/api/owner/coffee-shops", "User");
        using var listed = await fixture.Client.SendAsync(mine, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        fixture.Bus.Verify(b => b.InvokeAsync<Response<GetOwnerCoffeeShopsResponse>>(
            new GetOwnerCoffeeShopsQuery(fixture.UserId), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);

        using var detail = fixture.Request(HttpMethod.Get, $"/api/owner/coffee-shops/{shopId}", "User");
        using var denied = await fixture.Client.SendAsync(detail, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        fixture.Bus.Verify(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
            new GetOwnerCoffeeShopByIdQuery(shopId, fixture.UserId), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task MissingBusinessTypeIsRejectedBeforeDispatch()
    {
        await using var fixture = await Fixture.Start();
        using var submit = fixture.Request(HttpMethod.Post, "/api/owner/ownership-claims", "User",
            new { businessSlug = "test-shop", contact = "owner@example.com", evidence = "Official email" });
        using var response = await fixture.Client.SendAsync(submit, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.Bus.Invocations);
    }

    [Fact]
    public async Task RecordConstructorValidationRejectsOversizedInputBeforeDispatch()
    {
        await using var fixture = await Fixture.Start();
        using var claim = fixture.Request(HttpMethod.Post, "/api/owner/ownership-claims", "User",
            new { businessType = "Roaster", businessSlug = "test-roaster", contact = "owner@example.com", evidence = new string('x', 4001) });
        using var rejectedClaim = await fixture.Client.SendAsync(claim, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedClaim.StatusCode);

        using var decision = fixture.Request(HttpMethod.Post, $"/api/admin/ownership-claims/{Guid.NewGuid()}/approve", "Admin",
            new { reason = new string('x', 2001) });
        using var rejectedDecision = await fixture.Client.SendAsync(decision, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedDecision.StatusCode);
        Assert.Empty(fixture.Bus.Invocations);
    }

    [Fact]
    public async Task EveryShopOwnerWriteUsesAuthenticatedOwnerBeforeReportingMissingResource()
    {
        await using var fixture = await Fixture.Start();
        var shopId = Guid.NewGuid();
        fixture.Bus.Setup(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new NotFoundException("Shop not found."));
        foreach (var (method, suffix, body) in new (HttpMethod, string, object)[]
                 {
                     (HttpMethod.Put, "", new { name = "Changed", ownerUserId = Guid.NewGuid() }),
                     (HttpMethod.Put, "/primary-equipment", new { primaryEquipmentIds = Array.Empty<Guid>(), ownerUserId = Guid.NewGuid() }),
                     (HttpMethod.Put, "/photos/order", new { photoIds = Array.Empty<Guid>(), ownerUserId = Guid.NewGuid() }),
                     (HttpMethod.Post, "/photos", new { photos = Array.Empty<object>(), actorUserId = Guid.NewGuid(), ownerUserId = Guid.NewGuid() }),
                     (HttpMethod.Delete, "/photos", new { photoIds = Array.Empty<Guid>(), ownerUserId = Guid.NewGuid() })
                 })
        {
            using var request = fixture.Request(method, $"/api/owner/coffee-shops/{shopId}{suffix}", "User", body);
            using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(5, fixture.Bus.Invocations.Count);
        foreach (var call in fixture.Bus.Invocations)
        {
            var (target, owner) = call.Arguments[0] switch
            {
                UpdateOwnerCoffeeShopCommand c => (c.ShopId, (Guid?)c.OwnerUserId),
                SetShopPrimaryEquipmentCommand c => (c.ShopId, c.OwnerUserId),
                ReorderOwnerCoffeeShopPhotosCommand c => (c.ShopId, (Guid?)c.OwnerUserId),
                AddPublishedShopPhotosCommand c when c.ActorUserId == fixture.UserId => (c.ShopId, c.OwnerUserId),
                RemovePublishedShopPhotosCommand c => (c.ShopId, c.OwnerUserId),
                _ => throw new InvalidOperationException("Unexpected owner mutation.")
            };
            Assert.Equal(shopId, target);
            Assert.Equal(fixture.UserId, owner);
        }
    }

    [Fact]
    public async Task ManualShopAssignmentUsesTrustedActorAndReason()
    {
        await using var fixture = await Fixture.Start();
        var shopId = Guid.NewGuid();
        fixture.Bus.Setup(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                It.IsAny<AssignCoffeeShopOwnerCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<AdminPublishedShopDto>.Success(null!));
        using var request = fixture.Request(HttpMethod.Patch, $"/api/admin/shops/{shopId}/owner", "Admin",
            new { ownerUserId = (Guid?)null, reason = "No longer represents the business", actorUserId = Guid.NewGuid() });
        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        fixture.Bus.Verify(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
            new AssignCoffeeShopOwnerCommand(shopId, null, fixture.UserId, "No longer represents the business"),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    private static OwnershipClaimDto Claim(Guid id) => new(id, BusinessType.CoffeeShop, Guid.NewGuid(),
        "Test shop", "test-shop", Guid.NewGuid(), "owner@example.com", "Official email",
        OwnershipClaimStatus.Pending, DateTime.UtcNow, null, null, null, false, null, []);

    private sealed class Fixture(WebApplication app, HttpClient client, Mock<IMessageBus> bus) : IAsyncDisposable
    {
        private const string Secret = "ownership-http-test-secret-at-least-32-characters";
        public Guid UserId { get; } = Guid.NewGuid();
        public HttpClient Client { get; } = client;
        public Mock<IMessageBus> Bus { get; } = bus;

        public static async Task<Fixture> Start()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(OwnerOwnershipClaimsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["GatewayAuth:SecretKey"] = Secret });
            builder.Services.AddPresentation(builder.Configuration);
            var bus = new Mock<IMessageBus>();
            builder.Services.AddSingleton(bus.Object);
            var app = builder.Build();
            app.UseExceptionHandler();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync(TestContext.Current.CancellationToken);
            return new(app, new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }, bus);
        }

        public HttpRequestMessage Request(HttpMethod method, string path, string role, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.AddGatewayAuthHeader(Secret);
            request.Headers.Add(GatewayHeaderConsts.XUserId, UserId.ToString());
            request.Headers.Add(GatewayHeaderConsts.XUserRole, role);
            if (body is not null) request.Content = JsonContent.Create(body);
            return request;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
