using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class OwnerSlugTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShopsDbContext Db(bool enabled = true) => new(
        new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Options.Create(new PublicAddressOptions { Enabled = enabled }));
    private static CoffeeShop Shop(string name, string? address = null)
    {
        var shop = new CoffeeShop(null, name, null, PriceRange.Moderate, Guid.NewGuid());
        if (address is not null) shop.SetDraftLocation(Guid.NewGuid(), address);
        return shop;
    }

    [Fact]
    public async Task Rename_ReplacesSlug_OldAddressStopsResolving_AndCanBeReused()
    {
        await using var db = Db();
        var shop = Shop("Зерно", "Октябрьская 16"); db.Add(shop); await db.SaveChangesAsync(Ct);
        var service = new PublicAddressService(db);
        var old = shop.PublicSlug!;
        (await service.ResolveAsync(PublicAddressKind.CoffeeShop, old.ToUpperInvariant(), Ct))!.EntityId.Should().Be(shop.Id);
        var renamed = await service.ChangeAsync(PublicAddressKind.CoffeeShop, shop.Id,
            new("zerno-center", 1, "Rename"), Guid.NewGuid(), Ct);
        renamed.Revision.Should().Be(2); renamed.IsAlias.Should().BeFalse();
        shop.PublicSlug.Should().Be("zerno-center");
        (await service.ResolveAsync(PublicAddressKind.CoffeeShop, old, Ct)).Should().BeNull();
        var other = Shop("Other"); db.Add(other); await db.SaveChangesAsync(Ct);
        await service.ChangeAsync(PublicAddressKind.CoffeeShop, other.Id, new(old, 1, "Reuse"), Guid.NewGuid(), Ct);
        (await service.ResolveAsync(PublicAddressKind.CoffeeShop, old, Ct))!.EntityId.Should().Be(other.Id);
    }

    [Fact]
    public async Task Deletion_RemovesSlugWithOwner_AndReleasesIt()
    {
        await using var db=Db(); var shop=Shop("Unique"); db.Add(shop); await db.SaveChangesAsync(Ct);
        var old=shop.PublicSlug!; db.Remove(shop); await db.SaveChangesAsync(Ct);
        var service=new PublicAddressService(db);
        (await service.ResolveAsync(PublicAddressKind.CoffeeShop,old,Ct)).Should().BeNull();
        var replacement=Shop("Unique");db.Add(replacement);await db.SaveChangesAsync(Ct);
        replacement.PublicSlug.Should().Be(old);
    }

    [Fact]
    public async Task CollidingNames_ReceiveDifferentDeterministicSlugs_AndTakenRenameFails()
    {
        await using var db=Db();var a=new Roaster("Same");var b=new Roaster("Same");
        db.AddRange(a,b);await db.SaveChangesAsync(Ct);
        a.PublicSlug.Should().Be("same");b.PublicSlug.Should().Be(PublicSlug.Candidate("Same",b.Id,1));
        var service=new PublicAddressService(db);
        var taken=()=>service.ChangeAsync(PublicAddressKind.Roaster,b.Id,new("same",1,"Taken"),Guid.NewGuid(),Ct);
        (await taken.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(409);
        var stale=()=>service.ChangeAsync(PublicAddressKind.Roaster,a.Id,new("another",99,"Stale"),Guid.NewGuid(),Ct);
        (await stale.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task HiddenShop_UnpublishedZoneAndInactiveTag_DoNotResolve()
    {
        await using var db=Db();var shop=Shop("Hidden");shop.SetHidden(true);
        var tag=ShopTag.Create("laptop_friendly","Laptop");tag.Deactivate();
        var city=new City("Minsk");var zone=new CoffeeZone(city.Id,"Center",null,CoffeeZonePoint.Circle(53.9m,27.56m,400));
        zone.Publish();db.AddRange(shop,tag,city,zone);await db.SaveChangesAsync(Ct);zone.Archive();await db.SaveChangesAsync(Ct);
        var service=new PublicAddressService(db);
        foreach(var pair in new[]{(PublicAddressKind.CoffeeShop,(IPublicSlugOwner)shop),(PublicAddressKind.CoffeeZone,zone),(PublicAddressKind.ShopTag,tag)})
        {
            (await service.ResolveAsync(pair.Item1,pair.Item2.PublicSlug!,Ct)).Should().BeNull();
            (await service.GetByIdsAsync(pair.Item1,[pair.Item2.Id],Ct)).Should().BeEmpty();
        }
        tag.Slug.Should().Be("laptop_friendly");tag.PublicSlug.Should().Be("laptop-friendly");
    }

    [Fact]
    public async Task Backfill_DryRunDoesNotWrite_ReplayIsStable_AndBatchPreservesOrder()
    {
        await using var db=Db(false);var a=new City("Same");var b=new City("Same");db.AddRange(a,b);await db.SaveChangesAsync(Ct);
        var service=new PublicAddressService(db);
        // A new enabled context is normally used after rollout; test shares only its EF store.
        await using var enabled=new ShopsDbContext((DbContextOptions<ShopsDbContext>)db.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>(),
            Options.Create(new PublicAddressOptions { Enabled=true }));
        service=new PublicAddressService(enabled);
        var preview=await service.BackfillAsync(PublicAddressKind.City,null,100,true,Guid.NewGuid(),Ct);
        (await enabled.Cities.AsNoTracking().ToArrayAsync(Ct)).Should().OnlyContain(c=>c.PublicSlug==null);
        var applied=await service.BackfillAsync(PublicAddressKind.City,null,100,false,Guid.NewGuid(),Ct);
        applied.Items.Select(x=>x.Slug).Should().Equal(preview.Items.Select(x=>x.Slug));
        (await service.BackfillAsync(PublicAddressKind.City,null,100,false,Guid.NewGuid(),Ct)).Items.Should().OnlyContain(x=>x.AlreadyInitialized);
        (await service.GetByIdsAsync(PublicAddressKind.City,[b.Id,a.Id],Ct)).Select(x=>x.EntityId).Should().Equal(b.Id,a.Id);
    }

    [Fact]
    public void Model_StoresSlugsAndUniqueIndexesOnOwners_WithoutRegistryEntities()
    {
        using var db=new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=x").Options);
        foreach(var type in new[]{typeof(CoffeeShop),typeof(Roaster),typeof(City),typeof(CoffeeZone),typeof(CoffeeBean),typeof(Equipment),typeof(BrewMethod),typeof(ShopTag)})
        {
            var entity=db.Model.FindEntityType(type)!;
            entity.FindProperty(nameof(IPublicSlugOwner.PublicSlug)).Should().NotBeNull();
            entity.FindProperty(nameof(IPublicSlugOwner.SlugRevision))!.IsConcurrencyToken.Should().BeTrue();
            entity.GetIndexes().Should().Contain(i=>i.IsUnique&&i.Properties.Any(p=>p.Name==nameof(IPublicSlugOwner.PublicSlug)));
        }
        db.Model.GetEntityTypes().Should().NotContain(e=>e.Name.Contains("PublicAddressAggregate"));
    }
}
