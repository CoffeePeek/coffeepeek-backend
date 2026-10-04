using System.Reflection;
using CoffeePeek.ShopsService.Controllers;
using CoffeePeek.Shared.Auth.Constants;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Auth;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class AdminAuditedReadContractTests
{
    [Fact]
    public async Task IdentityReads_UseExactOwnerAndPreserveAliasMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var metadata = new PublicAddressMetadata(id, "canonical", "/coffee-shops/canonical", 3, true);
        var addresses = new FakeAddresses { Metadata = metadata, ExpectedId = id, ExpectedSlug = "alias" };
        var controller = new AdminShopPublicAddressesController(addresses);
        foreach (var result in new[] { await controller.GetById(id, ct), await controller.GetBySlug("alias", ct) })
            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<Response<PublicAddressMetadata>>()
                .Which.Data.Should().BeSameAs(metadata);
        addresses.Reads.Should().Be(2);
    }

    [Fact]
    public async Task IdentityReads_Unresolved_ReturnGeneric404_AndDisabledFeaturePropagates503()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var addresses = new FakeAddresses { ExpectedId = id, ExpectedSlug = "missing" };
        var controller = new AdminShopPublicAddressesController(addresses);
        (await controller.GetById(id, ct)).Should().BeOfType<NotFoundResult>();
        (await controller.GetBySlug("missing", ct)).Should().BeOfType<NotFoundResult>();
        addresses.Disabled = true;
        var action = () => controller.GetBySlug("missing", ct);
        (await action.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public void IdentityBridge_ReadsRequireModeratorAndPreserveAdminMutations()
    {
        var type = typeof(AdminRoastersController).Assembly.GetType("CoffeePeek.ShopsService.Controllers.AdminShopPublicAddressesController");
        type.Should().NotBeNull();
        type!.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/admin/shops");
        type.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(RoleConsts.Moderator);
        type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        type.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>()).Select(a => a.Template)
            .Should().BeEquivalentTo("{id:guid}/public-address", "by-slug/{slug}/public-address");
        typeof(AdminCoffeeShopsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(RoleConsts.Admin);
    }

    [Fact]
    public void Roaster_EditableReadUsesGuidAndModerator()
    {
        typeof(AdminRoastersController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(RoleConsts.Moderator);
        typeof(AdminRoastersController).GetMethods().SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>()).Select(a => a.Template)
            .Should().Contain("{id:guid}");
    }
    private sealed class FakeAddresses : IPublicAddressService
    {
        public PublicAddressMetadata? Metadata { get; init; }
        public Guid ExpectedId { get; init; }
        public string ExpectedSlug { get; init; } = "";
        public int Reads { get; private set; }
        public bool Disabled { get; set; }
        public Task<PublicAddressMetadata?> ResolveAsync(PublicAddressKind kind, string slug, CancellationToken ct)
        {
            if (Disabled) throw new PublicAddressException("disabled", 503);
            kind.Should().Be(PublicAddressKind.CoffeeShop);
            slug.Should().Be(ExpectedSlug);
            Reads++;
            return Task.FromResult(Metadata);
        }
        public Task<PublicAddressMetadata[]> GetByIdsAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct)
        {
            kind.Should().Be(PublicAddressKind.CoffeeShop);
            ids.Should().Equal(ExpectedId);
            Reads++;
            return Task.FromResult(Metadata is null ? Array.Empty<PublicAddressMetadata>() : new[] { Metadata });
        }
        public Task<PublicAddressMetadata[]> GetForReadAsync(PublicAddressKind kind, Guid[] ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid[]> ResolveIdsAsync(PublicAddressKind kind, string[] slugs, CancellationToken ct) => throw new NotSupportedException();
        public Task<PublicAddressMetadata> ChangeAsync(PublicAddressKind kind, Guid id, ChangePublicAddressRequest request, Guid actorId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PublicAddressHistoryItem[]> HistoryAsync(PublicAddressKind kind, Guid id, long afterRevision, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<PublicAddressBackfillResult> BackfillAsync(PublicAddressKind kind, Guid? afterId, int limit, bool dryRun, Guid actorId, CancellationToken ct) => throw new NotSupportedException();
    }
}
