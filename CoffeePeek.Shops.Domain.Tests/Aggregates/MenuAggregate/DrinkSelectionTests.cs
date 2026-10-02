using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.MenuAggregate;

public class DrinkSelectionTests
{
    private sealed class Catalog : IDrinkCatalog
    {
        public int Calls { get; private set; }
        public Task<DrinkCatalogEntry?> FindAsync(string slug, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<DrinkCatalogEntry?>(slug switch
            {
                "latte" => new("Латте", "Latte", true),
                "inactive" => new("Неактивный", "Inactive", false),
                _ => null
            });
        }
    }

    [Fact]
    public async Task Resolve_NormalizesSlugAndSnapshotsCatalogNames()
    {
        var drink = await DrinkSelection.ResolveAsync(" LATTE ", null, new Catalog(), TestContext.Current.CancellationToken);
        drink.Should().Be(new DrinkSelection("latte", null, "Латте", "Latte"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("other", "  Эспрессо-тоник  ")]
    public async Task Resolve_OptionalAndOtherDoNotRequireCatalog(string? slug, string? name)
    {
        var catalog = new Catalog();
        var drink = await DrinkSelection.ResolveAsync(slug, name, catalog, TestContext.Current.CancellationToken);
        catalog.Calls.Should().Be(0);
        if (slug is null) drink.Should().BeNull();
        else drink!.CustomName.Should().Be("Эспрессо-тоник");
    }

    [Theory]
    [InlineData("other", null)]
    [InlineData("other", "   ")]
    [InlineData("latte", "Custom")]
    [InlineData(null, "Custom")]
    [InlineData("missing", null)]
    [InlineData("inactive", null)]
    public async Task Resolve_RejectsInvalidSelections(string? slug, string? name)
    {
        var act = () => DrinkSelection.ResolveAsync(slug, name, new Catalog(), TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Resolve_RejectsOverlongCustomName()
    {
        var act = () => DrinkSelection.ResolveAsync("other", new string('a', 101), new Catalog(), TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public void CatalogUpdate_KeepsIdentityAndDoesNotChangeSavedSelection()
    {
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
            CoffeeDrinkCategory.Espresso, "latte", 10);
        var checkIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        checkIn.SetDrink(new(drink.Slug, null, drink.NameRu, drink.NameEn));
        drink.Update("Новое название", "New name", CoffeeDrinkCategory.Espresso, "latte", 20, false);
        drink.Slug.Should().Be("latte");
        drink.IsActive.Should().BeFalse();
        checkIn.DrinkNameRu.Should().Be("Латте");
    }

    [Theory]
    [InlineData("other")]
    [InlineData("invalid slug")]
    [InlineData(" ")]
    public void Catalog_RejectsReservedOrInvalidSlugs(string slug)
    {
        var act = () => CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), slug, "Кофе", "Coffee",
            CoffeeDrinkCategory.Espresso, "", 0);
        act.Should().Throw<DomainException>();
    }
}
