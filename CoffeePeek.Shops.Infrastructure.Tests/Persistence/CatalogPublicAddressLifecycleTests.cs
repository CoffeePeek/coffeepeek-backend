using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CatalogPublicAddressLifecycleTests
{
    [Fact]
    public async Task CatalogIdentities_SurviveDisplayEditsResolveAliasesAndReserveDeletedSlugs()
    {
        var flags = new PublicAddressOptions { Enabled = true };
        await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Options.Create(flags));
        var bean = new CoffeeBean("Arabica");
        var category = new EquipmentCategory();
        var equipment = new Equipment("La Marzocco", "Linea", category);
        var method = new BrewMethod("V60", BrewMethodCategory.Gravity);
        var tag = ShopTag.Create("dog-friendly", "Dogs welcome");
        db.AddRange(bean, equipment, method, tag);
        db.Entry(category).Property(nameof(EquipmentCategory.Name)).CurrentValue = "Espresso";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var cases = new[]
        {
            (PublicAddressKind.CoffeeBean, bean.Id, "arabica"),
            (PublicAddressKind.Equipment, equipment.Id, "la-marzocco-linea"),
            (PublicAddressKind.BrewMethod, method.Id, "v60"),
            (PublicAddressKind.ShopTag, tag.Id, "dog-friendly")
        };
        bean.Update("New bean display name");
        equipment.Update("New brand", "New model", category);
        method.Update("New brew method display name", BrewMethodCategory.Gravity);
        tag.Update("New tag display name", null, 0, true);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new PublicAddressService(db);
        foreach (var (kind, id, slug) in cases)
        {
            (await service.ResolveAsync(kind, slug, TestContext.Current.CancellationToken))!.EntityId.Should().Be(id);
            await service.ChangeAsync(kind, id, new(slug + "-canonical", 1, "Canonical address test"), Guid.NewGuid(), TestContext.Current.CancellationToken);
            var alias = await service.ResolveAsync(kind, slug.ToUpperInvariant(), TestContext.Current.CancellationToken);
            alias!.IsAlias.Should().BeTrue();
            (await service.ResolveIdsAsync(kind, [slug, slug + "-canonical"], TestContext.Current.CancellationToken)).Should().Equal(id);
            (await service.GetForReadAsync(kind, [id], TestContext.Current.CancellationToken)).Single().Slug.Should().Be(slug + "-canonical");
        }
        tag.Deactivate();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        (await service.ResolveAsync(PublicAddressKind.ShopTag, "dog-friendly", TestContext.Current.CancellationToken)).Should().BeNull();
        var inactiveFilter = () => service.ResolveIdsAsync(PublicAddressKind.ShopTag, ["dog-friendly"], TestContext.Current.CancellationToken);
        (await inactiveFilter.Should().ThrowAsync<PublicAddressException>()).Which.StatusCode.Should().Be(404);
        flags.Enabled = false;
        db.RemoveRange(bean, equipment, method, tag);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        flags.Enabled = true;
        var replacement = new CoffeeBean("Arabica");
        db.Add(replacement);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var address = (await service.GetForReadAsync(PublicAddressKind.CoffeeBean, [replacement.Id], TestContext.Current.CancellationToken)).Single();
        address.Slug.Should().StartWith("arabica-").And.NotContain(replacement.Id.ToString("N")[..8]);
        (await db.CoffeeBeanPublicAddresses.SingleAsync(x => x.Slug == "arabica", TestContext.Current.CancellationToken))
            .State.Should().Be(PublicAddressState.Tombstone);
    }
}
