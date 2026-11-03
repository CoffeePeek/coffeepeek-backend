using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.Shops.Application.PublicAddresses;
using Moq;

namespace CoffeePeek.ShopsService.Tests;

public sealed partial class DiscoveryHttpContractTests
{
    [Theory]
    [InlineData("GET", "/api/v1/saved-collections")]
    [InlineData("GET", "/api/v1/saved-collections/featured")]
    [InlineData("POST", "/api/v1/saved-collections")]
    [InlineData("PUT", "/api/v1/saved-collections/featured")]
    [InlineData("PUT", "/api/v1/saved-collections/membership/roaster/sample")]
    public async Task SavedLibraryRequiresAuthenticatedOwner(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = Content("{}") };
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        saved.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnerPickerLoadsMembershipInOneReadAndGlobalFavoritesAreNotRead()
    {
        var ct = TestContext.Current.CancellationToken; var owner = Guid.NewGuid();
        Authenticate(owner, RoleConsts.User);
        var id = Guid.NewGuid();
        saved.Setup(s => s.ListAsync(owner, "roaster", "sample", It.IsAny<CancellationToken>())).ReturnsAsync([
            new(id, "Private list", null, "private", null, 1, 1, true, false, null)
        ]);
        using var response = await client.GetAsync("/api/v1/saved-collections?kind=roaster&slug=sample", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var item = json.RootElement.GetProperty("data")[0];
        Assert.True(item.GetProperty("containsItem").GetBoolean());
        Assert.Equal("list", item.GetProperty("mode").GetString());
        Assert.Equal("private", item.GetProperty("visibility").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("canonicalPath").ValueKind);
        favorites.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublicProfileReturnsOnlyExplicitFeaturedCollectionOrNullWithoutGlobalFavoriteReads()
    {
        var ct = TestContext.Current.CancellationToken; var owner = Guid.NewGuid();
        profiles.Setup(p => p.ResolveAsync("reader", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicAddressMetadata(owner, "reader", "/users/reader", 1));
        saved.Setup(s => s.PublicFeaturedAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync((SavedCollectionDto?)null);
        using var none = await client.GetAsync("/api/v1/profiles/reader/collection", ct);
        Assert.Equal(HttpStatusCode.OK, none.StatusCode);
        using var noneJson = JsonDocument.Parse(await none.Content.ReadAsStringAsync(ct));
        Assert.Equal(JsonValueKind.Null, noneJson.RootElement.GetProperty("data").ValueKind);
        var id = Guid.NewGuid();
        saved.Setup(s => s.PublicFeaturedAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(
            new SavedCollectionDto(id, "Featured", "Public description", "public", null, 0, 2, true,
                $"/collections/{id:N}", DateTime.UtcNow, DateTime.UtcNow, []));
        using var featured = await client.GetAsync("/api/v1/profiles/reader/collection", ct);
        using var json = JsonDocument.Parse(await featured.Content.ReadAsStringAsync(ct));
        Assert.Equal("public", json.RootElement.GetProperty("data").GetProperty("visibility").GetString());
        Assert.Equal("list", json.RootElement.GetProperty("data").GetProperty("mode").GetString());
        Assert.Contains("no-store", featured.Headers.CacheControl!.ToString());
        favorites.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MetadataConflictKeepsDiscoveryErrorContractAndPinsAuthenticatedOwner()
    {
        var ct = TestContext.Current.CancellationToken; var owner = Guid.NewGuid(); var id = Guid.NewGuid();
        Authenticate(owner, RoleConsts.User);
        bus.Setup(b => b.InvokeAsync<SavedCollectionDto?>(It.Is<UpdateSavedCollectionCommand>(c => c.OwnerId == owner && c.Id == id),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new DiscoveryException("Changed", 409, "COLLECTION_CONFLICT"));
        using var response = await client.PutAsJsonAsync($"/api/v1/saved-collections/{id}",
            new { title = "Valid", visibility = "private", expectedRevision = 1, ownerId = Guid.NewGuid() }, ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("COLLECTION_CONFLICT", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AnonymousCommonReadUsesOnlyTheGuardedPublicLibraryLookupAndPrivateIs404NoStore()
    {
        var ct = TestContext.Current.CancellationToken; var id = Guid.NewGuid();
        snapshots.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((FavoriteCollectionDto?)null);
        saved.Setup(s => s.GetPublicAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((FavoriteCollectionDto?)null);
        using var response = await client.GetAsync($"/api/v1/favorite-collections/{id}", ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        saved.Verify(s => s.GetPublicAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        saved.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnonymousCommonReadKeepsLibraryMetadataAndSerializesTheTwoReturnedItems()
    {
        var ct = TestContext.Current.CancellationToken; var id = Guid.NewGuid();
        snapshots.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((FavoriteCollectionDto?)null);
        saved.Setup(s => s.GetPublicAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new FavoriteCollectionDto
            { Id = id, Title = "Published library", CanonicalPath = $"/collections/{id:N}", CreatedAtUtc = DateTime.UtcNow,
                Items = [
                    new() { Kind = "roaster", Name = "First", Address = new() { Slug = "first", CanonicalPath = "/roasters/first", Revision = 1 }, CreatedAtUtc = DateTime.UtcNow },
                    new() { Kind = "roaster", Name = "Second", Address = new() { Slug = "second", CanonicalPath = "/roasters/second", Revision = 1 }, CreatedAtUtc = DateTime.UtcNow }
                ], Mode = "list", Visibility = "public", Description = "Public description", Revision = 3 });
        using var response = await client.GetAsync($"/api/v1/favorite-collections/{id}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var item = json.RootElement.GetProperty("data");
        Assert.Equal("list", item.GetProperty("mode").GetString());
        Assert.Equal("public", item.GetProperty("visibility").GetString());
        Assert.Equal("Public description", item.GetProperty("description").GetString());
        Assert.Equal(2, item.GetProperty("items").GetArrayLength());
        Assert.Equal(2, item.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task PrivateOrForeignCoverGateNeverRequestsMediaBytesAndPublicMissDisablesCaching()
    {
        var ct = TestContext.Current.CancellationToken; var id = Guid.NewGuid(); var photo = Guid.NewGuid();
        saved.Setup(s => s.CoverAsync(null, id, photo, It.IsAny<CancellationToken>())).ReturnsAsync((CollectionCoverAccess?)null);
        using var publicRead = await client.GetAsync($"/api/v1/favorite-collections/{id}/cover/{photo}", ct);
        Assert.Equal(HttpStatusCode.NotFound, publicRead.StatusCode);
        Assert.Contains("no-store", publicRead.Headers.CacheControl!.ToString());
        using var guestOwnerRead = await client.GetAsync($"/api/v1/saved-collections/{id}/cover/{photo}", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, guestOwnerRead.StatusCode);
        var stranger = Guid.NewGuid(); Authenticate(stranger, RoleConsts.User);
        saved.Setup(s => s.CoverAsync(stranger, id, photo, It.IsAny<CancellationToken>())).ReturnsAsync((CollectionCoverAccess?)null);
        using var foreign = await client.GetAsync($"/api/v1/saved-collections/{id}/cover/{photo}", ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        coverHttp.VerifyNoOtherCalls();
    }
}
