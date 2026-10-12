using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.MenuAggregate;

public sealed class ShopMenuItemValidationTests
{
    [Fact]
    public void Present_RequiresBothPositiveValues_OnEveryDomainEntryPoint()
    {
        var drinkId = Guid.NewGuid();
        var invalid = new (decimal? Price, int? Volume)[]
        {
            (null, 200), (5m, null), (0m, 200), (-1m, 200), (5m, 0), (5m, -1)
        };

        foreach (var (price, volume) in invalid)
        {
            Assert.Throws<DomainException>(() => ShopMenuItem.Create(
                drinkId, MenuItemAvailability.Present, price, volume, MenuItemSource.Parsed));
        }

        var item = ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, 5m, 200, MenuItemSource.Parsed);
        foreach (var (price, volume) in invalid)
        {
            Assert.Throws<DomainException>(() => item.ApplyParsed(price, volume));
            Assert.Throws<DomainException>(() => item.ApplyManual(MenuItemAvailability.Present, price, volume));
            Assert.Equal(5m, item.Price);
            Assert.Equal(200, item.VolumeMl);
            Assert.Equal(MenuItemSource.Parsed, item.Source);
        }
    }

    [Fact]
    public void AbsentAndUnknown_RequireNoValues_AndCreateMatchesManualUpdate()
    {
        var drinkId = Guid.NewGuid();
        foreach (var availability in new[] { MenuItemAvailability.Absent, MenuItemAvailability.Unknown })
        {
            Assert.Throws<DomainException>(() => ShopMenuItem.Create(
                drinkId, availability, 5m, null, MenuItemSource.Manual));
            Assert.Throws<DomainException>(() => ShopMenuItem.Create(
                drinkId, availability, null, 200, MenuItemSource.Manual));

            var created = ShopMenuItem.Create(drinkId, availability, null, null, MenuItemSource.Manual);
            Assert.Null(created.Price);
            Assert.Null(created.VolumeMl);

            var existing = ShopMenuItem.Create(drinkId, MenuItemAvailability.Present, 5m, 200,
                MenuItemSource.Parsed);
            existing.ApplyManual(availability, null, null);
            Assert.Equal(created.Availability, existing.Availability);
            Assert.Equal(created.Price, existing.Price);
            Assert.Equal(created.VolumeMl, existing.VolumeMl);
            Assert.Equal(MenuItemSource.Manual, existing.Source);
        }
    }

    [Fact]
    public void InvalidAvailabilityAndSource_AreRejectedBeforeChangingMenu()
    {
        var drinkId = Guid.NewGuid();
        var menu = ShopMenu.Create(Guid.NewGuid());
        Assert.Throws<DomainException>(() => menu.ApplyManualItem(
            drinkId, (MenuItemAvailability)99, 5m, 200, Guid.NewGuid()));
        Assert.Empty(menu.Items);
        Assert.Equal(MenuParseStatus.None, menu.ParseStatus);
        Assert.Throws<DomainException>(() => ShopMenuItem.Create(
            drinkId, MenuItemAvailability.Present, 5m, 200, (MenuItemSource)99));
    }
}
