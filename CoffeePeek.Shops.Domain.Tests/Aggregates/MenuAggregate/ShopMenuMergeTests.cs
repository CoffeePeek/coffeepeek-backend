using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.MenuAggregate;

public class ShopMenuMergeTests
{
    private static readonly Guid Espresso = Guid.NewGuid();
    private static readonly Guid Latte = Guid.NewGuid();
    private static readonly Guid Raf = Guid.NewGuid();

    private static ShopMenuItem Parsed(Guid drink, decimal price) =>
        ShopMenuItem.Create(drink, MenuItemAvailability.Present, price, null, MenuItemSource.Parsed);

    [Fact]
    public void ReParse_KeepsManualPricesAndItemsFromEarlierPhotos()
    {
        // Regression: each parse cleared the whole menu, dropping earlier photos' drinks and manual edits.
        var menu = ShopMenu.Create(Guid.NewGuid());
        menu.MergeParsedItems([Parsed(Espresso, 5), Parsed(Latte, 7)], null, null, null);
        menu.ApplyManualItem(Latte, MenuItemAvailability.Present, 8, null, Guid.NewGuid());

        menu.MergeParsedItems([Parsed(Latte, 6), Parsed(Raf, 9)], null, null, null);

        menu.Items.Should().HaveCount(3);
        menu.Items.Single(i => i.DrinkDefinitionId == Espresso).Price.Should().Be(5);
        menu.Items.Single(i => i.DrinkDefinitionId == Latte).Price.Should().Be(8, "manual edits outrank the parser");
        menu.ParseStatus.Should().Be(MenuParseStatus.Ready);
    }

    [Fact]
    public void Parse_WithDuplicateDrinks_KeepsOneRowPerDrink()
    {
        var menu = ShopMenu.Create(Guid.NewGuid());

        menu.MergeParsedItems([Parsed(Espresso, 5), Parsed(Espresso, 6)], null, null, null);
        menu.ApplyParsedItems([Parsed(Latte, 5), Parsed(Latte, 6)], null, null, null);

        menu.Items.Should().ContainSingle().Which.Price.Should().Be(6);
    }

    [Fact]
    public void CompleteParse_WithNoMatches_MovesOutOfPending()
    {
        var menu = ShopMenu.Create(Guid.NewGuid());
        menu.MarkParsePending(DateTime.UnixEpoch);

        menu.CompleteParse("[]", null, null);

        menu.ParseStatus.Should().Be(MenuParseStatus.Ready);
    }
}
