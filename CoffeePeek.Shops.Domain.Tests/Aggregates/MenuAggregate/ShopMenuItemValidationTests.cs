using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.MenuAggregate;

public sealed class ShopMenuItemValidationTests
{
    [Fact]
    public void Present_RequiresPositivePrice_OnEveryDomainEntryPoint()
    {
        var drinkId = Guid.NewGuid();
        var item = ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, 5m, MenuItemSource.Parsed);
        foreach (var price in new decimal?[] { null, 0, -1, 100_000_000m })
        {
            Assert.Throws<DomainException>(() => ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, price, MenuItemSource.Parsed));
            Assert.Throws<DomainException>(() => item.ApplyParsed(price));
            Assert.Throws<DomainException>(() => item.ApplyManual(MenuItemAvailability.Present, price));
            Assert.Equal(5m, item.Price);
            Assert.Equal(MenuItemSource.Parsed, item.Source);
        }
    }

    [Theory]
    [InlineData(MenuItemAvailability.Absent)]
    [InlineData(MenuItemAvailability.Unknown)]
    public void AbsentAndUnknown_RequireNoPrice_AndCreateMatchesManualUpdate(MenuItemAvailability availability)
    {
        var drinkId = Guid.NewGuid();
        Assert.Throws<DomainException>(() => ShopMenuItem.Create(drinkId, availability, 5m, MenuItemSource.Manual));
        var created = ShopMenuItem.Create(drinkId, availability, null, MenuItemSource.Manual);
        var existing = ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, 5m, MenuItemSource.Parsed);
        existing.ApplyManual(availability, null);
        Assert.Equal(created.Availability, existing.Availability);
        Assert.Equal(created.Price, existing.Price);
        Assert.Equal(MenuItemSource.Manual, existing.Source);
    }

    [Fact]
    public void InvalidAvailabilityAndSource_AreRejectedBeforeChangingMenu()
    {
        var drinkId = Guid.NewGuid();
        var menu = ShopMenu.Create(Guid.NewGuid());
        Assert.Throws<DomainException>(() => menu.ApplyManualItem(drinkId, (MenuItemAvailability)99, 5m, Guid.NewGuid()));
        Assert.Empty(menu.Items);
        Assert.Equal(MenuParseStatus.None, menu.ParseStatus);
        Assert.Throws<DomainException>(() => ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, 5m, (MenuItemSource)99));
    }
}
