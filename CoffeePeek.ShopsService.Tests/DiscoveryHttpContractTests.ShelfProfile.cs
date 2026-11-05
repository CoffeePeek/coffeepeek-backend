using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shops.Application.Features.Shelf.SetProfileVisibility;
using Moq;

namespace CoffeePeek.ShopsService.Tests;

public sealed partial class DiscoveryHttpContractTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task ShelfVisibilityRequiresAuthentication(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/shelf/profile-visibility") { Content = Content("{\"isVisible\":true}") };
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        shelfProfiles.VerifyNoOtherCalls(); bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isVisible\":null}")]
    [InlineData("{\"isVisible\":\"true\"}")]
    public async Task ShelfVisibilityRequiresAnExplicitBoolean(string body)
    {
        Authenticate(Guid.NewGuid(), RoleConsts.User);
        using var response = await client.PutAsync("/api/v1/shelf/profile-visibility", Content(body), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        shelfProfiles.VerifyNoOtherCalls(); bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShelfVisibilityReadWriteUsesOnlyTheSignedInOwner(bool visible)
    {
        var ct = TestContext.Current.CancellationToken; var owner = Guid.NewGuid();
        Authenticate(owner, RoleConsts.User);
        shelfProfiles.Setup(s => s.VisibilityAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(new ShelfProfileVisibilityDto(true));
        shelfProfiles.Setup(s => s.SetVisibilityAsync(owner, visible, It.IsAny<CancellationToken>())).ReturnsAsync(new ShelfProfileVisibilityDto(visible));
        bus.Setup(b => b.InvokeAsync<ShelfProfileVisibilityDto>(It.IsAny<SetShelfProfileVisibilityCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Returns((object command, CancellationToken token, TimeSpan? _) => SetShelfProfileVisibilityHandler.Handle((SetShelfProfileVisibilityCommand)command, shelfProfiles.Object, token));
        using var read = await client.GetAsync("/api/v1/shelf/profile-visibility", ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Contains("no-store", read.Headers.CacheControl!.ToString());
        using var initial = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
        Assert.True(initial.RootElement.GetProperty("data").GetProperty("isVisible").GetBoolean());
        using var response = await client.PutAsync("/api/v1/shelf/profile-visibility", Content($"{{\"isVisible\":{visible.ToString().ToLowerInvariant()}}}"), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal(visible, json.RootElement.GetProperty("data").GetProperty("isVisible").GetBoolean());
        shelfProfiles.Verify(s => s.SetVisibilityAsync(owner, visible, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicShelfReturnsNullWhenHiddenAndOnlyWhitelistedFieldsWhenVisible()
    {
        var ct = TestContext.Current.CancellationToken; var owner = Guid.NewGuid(); var bag = Guid.NewGuid();
        profiles.Setup(p => p.ResolveAsync("author", It.IsAny<CancellationToken>())).ReturnsAsync(new PublicAddressMetadata(owner, "author", "/users/author", 1));
        shelfProfiles.Setup(s => s.PublicAsync(owner, "author", It.IsAny<CancellationToken>())).ReturnsAsync((PublicShelfDto?)null);
        using var hidden = await client.GetAsync("/api/v1/profiles/author/shelf", ct);
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        using var hiddenJson = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync(ct));
        Assert.Equal(JsonValueKind.Null, hiddenJson.RootElement.GetProperty("data").ValueKind);
        shelfProfiles.Setup(s => s.PublicAsync(owner, "author", It.IsAny<CancellationToken>())).ReturnsAsync(
            new PublicShelfDto([new(bag, "Coffee", "Roaster", "coffee", null)], 1));
        using var visible = await client.GetAsync("/api/v1/profiles/author/shelf", ct);
        Assert.Contains("no-store", visible.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await visible.Content.ReadAsStringAsync(ct));
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "coffeeSlug", "id", "name", "photoUrl", "roasterName" }, data.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.DoesNotContain(owner.ToString(), data.ToString());
        favorites.VerifyNoOtherCalls(); saved.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InaccessibleProfileIsNotQueriedForShelfOrPhoto()
    {
        var ct = TestContext.Current.CancellationToken;
        using var shelf = await client.GetAsync("/api/v1/profiles/blocked/shelf", ct);
        using var photo = await client.GetAsync($"/api/v1/profiles/blocked/shelf/bags/{Guid.NewGuid()}/photos/{Guid.NewGuid()}", ct);
        Assert.Equal(HttpStatusCode.NotFound, shelf.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, photo.StatusCode);
        shelfProfiles.VerifyNoOtherCalls(); coverHttp.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedPublicShelfReadCannotOutliveProfileAccessOrAuthorIdentity(bool reassigned)
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        PublicAddressMetadata? current = new(owner, "author", "/users/author", 1);
        profiles.Setup(p => p.ResolveAsync("author", It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<PublicShelfDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        shelfProfiles.Setup(s => s.PublicAsync(owner, "author", It.IsAny<CancellationToken>()))
            .Returns(() => { started.TrySetResult(); return delayed.Task; });
        var pending = client.GetAsync("/api/v1/profiles/author/shelf", ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        current = reassigned ? new(Guid.NewGuid(), "author", "/users/author", 2) : null;
        delayed.SetResult(new([new(Guid.NewGuid(), "Must stay private", null, null, null)], 1));
        using var response = await pending;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Must stay private", await response.Content.ReadAsStringAsync(ct));
        profiles.Verify(p => p.ResolveAsync("author", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
