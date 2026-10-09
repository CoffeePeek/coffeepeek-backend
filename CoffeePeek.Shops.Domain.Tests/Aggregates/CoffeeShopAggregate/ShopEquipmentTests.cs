using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeShopAggregate;

public sealed class ShopEquipmentTests
{
    [Fact]
    public void PrimaryEquipmentBelongsToAssignmentNotSharedCatalogEntry()
    {
        var category = Category(1);
        var first = new Equipment("Brand", "A", category);
        var second = new Equipment("Brand", "B", category);
        var shopA = Shop();
        var shopB = Shop();

        shopA.SetEquipment([first, second], [first.Id]).IsSuccess.Should().BeTrue();
        shopB.SetEquipment([first, second], [second.Id]).IsSuccess.Should().BeTrue();

        shopA.ShopEquipments.Single(a => a.IsPrimary).EquipmentId.Should().Be(first.Id);
        shopB.ShopEquipments.Single(a => a.IsPrimary).EquipmentId.Should().Be(second.Id);
    }

    [Fact]
    public void OnePrimaryPerCategoryAllowsIndependentCategories()
    {
        var grinder = new Equipment("Brand", "Grinder", Category(1));
        var machine = new Equipment("Brand", "Machine", Category(2));
        var shop = Shop();

        shop.SetEquipment([grinder, machine], [grinder.Id, machine.Id]).IsSuccess.Should().BeTrue();

        shop.ShopEquipments.Count(a => a.IsPrimary).Should().Be(2);
        shop.ShopEquipments.Select(a => a.CategoryId).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void DuplicatePrimaryInSameCategoryRejectsWithoutChangingAssignments()
    {
        var category = Category(1);
        var first = new Equipment("Brand", "A", category);
        var second = new Equipment("Brand", "B", category);
        var shop = Shop();
        shop.SetEquipment([first], [first.Id]).IsSuccess.Should().BeTrue();

        var result = shop.SetEquipment([first, second], [first.Id, second.Id]);

        result.IsFailed.Should().BeTrue();
        shop.ShopEquipments.Should().ContainSingle();
        shop.ShopEquipments.Single().EquipmentId.Should().Be(first.Id);
        shop.ShopEquipments.Single().IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void ReplacementPreservesRetainedPrimaryUnlessExplicitlyCleared()
    {
        var category = Category(1);
        var first = new Equipment("Brand", "A", category);
        var second = new Equipment("Brand", "B", category);
        var shop = Shop();
        shop.SetEquipment([first], [first.Id]);

        shop.SetEquipment([first, second]).IsSuccess.Should().BeTrue();
        shop.ShopEquipments.Single(a => a.IsPrimary).EquipmentId.Should().Be(first.Id);
        shop.SetEquipment([first, second], []).IsSuccess.Should().BeTrue();
        shop.ShopEquipments.Should().OnlyContain(a => !a.IsPrimary);
    }

    private static CoffeeShop Shop() =>
        new(Guid.NewGuid(), "Equipment shop", null, PriceRange.Moderate, Guid.NewGuid());

    private static EquipmentCategory Category(int id)
    {
        var category = new EquipmentCategory();
        typeof(EquipmentCategory).GetProperty(nameof(EquipmentCategory.Id))!.SetValue(category, id);
        return category;
    }
}
