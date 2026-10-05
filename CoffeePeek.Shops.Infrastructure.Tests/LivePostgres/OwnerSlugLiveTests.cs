using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class OwnerSlugLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public OwnerSlugLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(PublicAddressKind.CoffeeShop)]
    [InlineData(PublicAddressKind.Roaster)]
    [InlineData(PublicAddressKind.City)]
    [InlineData(PublicAddressKind.CoffeeZone)]
    [InlineData(PublicAddressKind.CoffeeBean)]
    [InlineData(PublicAddressKind.Equipment)]
    [InlineData(PublicAddressKind.BrewMethod)]
    [InlineData(PublicAddressKind.ShopTag)]
    public async Task EveryKind_StoresCurrentSlug_RenameDropsOldAddress(PublicAddressKind kind)
    {
        var id = await fixture.AddOwnerAsync(kind, "Primary " + kind, true, Ct);
        await using var db = fixture.Context();
        var service = new PublicAddressService(db);
        var initial = (await service.GetByIdsAsync(kind, [id], Ct)).Single();
        (await service.ResolveAsync(kind, initial.Slug.ToUpperInvariant(), Ct))!.EntityId.Should().Be(id);
        var next = "renamed-" + kind.ToString().ToLowerInvariant();
        var changed = await service.ChangeAsync(kind, id, new(next, 1, "Current URL"), Guid.NewGuid(), Ct);
        changed.Revision.Should().Be(2);
        (await service.ResolveAsync(kind, initial.Slug, Ct)).Should().BeNull();
        (await service.ResolveAsync(kind, next, Ct))!.IsAlias.Should().BeFalse();
        (await service.ResolveIdsAsync(kind, [next], Ct)).Should().Equal(id);
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentCreates_UseUniquePrimarySlugs_AndDatabaseRejectsDuplicates()
    {
        var ids = await Task.WhenAll(fixture.AddOwnerAsync(PublicAddressKind.Roaster, "Concurrent Primary", true, Ct),
            fixture.AddOwnerAsync(PublicAddressKind.Roaster, "Concurrent Primary", true, Ct));
        await using var db = fixture.Context();
        var owners = await db.Roasters.Where(r => ids.Contains(r.Id)).ToArrayAsync(Ct);
        owners.Select(r => r.PublicSlug).Distinct().Should().HaveCount(2);
        owners[1].SetPublicSlug(owners[0].PublicSlug!);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        LivePostgresServer.FindPostgres(error)!.ConstraintName.Should().Be("IX_Roasters_Slug");
    }

    [Fact]
    public async Task DeletedOwner_ReleasesSlugWithoutLeavingAnAddressRow()
    {
        var id = await fixture.AddOwnerAsync(PublicAddressKind.Roaster, "Released Primary", true, Ct);
        await using var db = fixture.Context();
        var owner = await db.Roasters.SingleAsync(r => r.Id == id, Ct);
        var slug = owner.PublicSlug!;
        db.Remove(owner); await db.SaveChangesAsync(Ct);
        var replacementId = await fixture.AddOwnerAsync(PublicAddressKind.Roaster, "Released Primary", true, Ct);
        (await new PublicAddressService(db).ResolveAsync(PublicAddressKind.Roaster, slug, Ct))!.EntityId.Should().Be(replacementId);
    }
}
