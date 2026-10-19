using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.MenuAggregate;

public sealed class ShopMenuApprovedSnapshotTests
{
    [Fact]
    public void ApprovalPreservesMatchingDrinkRowAndRemovesOnlyMissingDrinks()
    {
        var drink = Guid.NewGuid();
        var removedDrink = Guid.NewGuid();
        var addedDrink = Guid.NewGuid();
        var menu = ShopMenu.Create(Guid.NewGuid());
        var moderator = Guid.NewGuid();
        menu.ApplyApprovedManualSnapshot(
            [Item(drink, 3), Item(removedDrink, 4)], DateTime.UtcNow, moderator);
        var originalId = menu.Items.Single(x => x.DrinkDefinitionId == drink).Id;

        menu.ApplyApprovedManualSnapshot(
            [Item(drink, 5), Item(addedDrink, 6)], DateTime.UtcNow, moderator);

        Assert.Equal(2, menu.InputRevision);
        Assert.Equal(2, menu.Items.Count);
        var retained = Assert.Single(menu.Items.Where(x => x.DrinkDefinitionId == drink));
        Assert.Equal(originalId, retained.Id);
        Assert.Equal(5, retained.Price);
        Assert.DoesNotContain(menu.Items, x => x.DrinkDefinitionId == removedDrink);
        Assert.Contains(menu.Items, x => x.DrinkDefinitionId == addedDrink);
    }

    private static ShopMenuItem Item(Guid drinkId, decimal price) =>
        ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, price, MenuItemSource.Manual);
}
