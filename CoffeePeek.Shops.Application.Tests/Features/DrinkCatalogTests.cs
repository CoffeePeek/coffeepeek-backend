#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.Catalogs.Drinks;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Shops.Application.Tests.Features;

public class DrinkCatalogTests
{
    [Fact]
    public async Task PublicCatalog_IncludesOtherAndUsesRepositoryNames()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalog = new Mock<IQueryCoffeeDrinkRepository>();
        catalog.Setup(c => c.GetActiveAsync(ct)).ReturnsAsync([
            CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "special", "Особый", "Special", CoffeeDrinkCategory.Filter, "", 10)]);
        var result = await GetConsumedDrinksHandler.Handle(new(), catalog.Object, ct);
        result.Data!.Select(d => d.Slug).Should().Equal("special", "other");
        result.Data![0].NameRu.Should().Be("Особый");
        result.Data![1].Category.Should().BeNull();
    }

    [Fact]
    public async Task AdminUpdate_DeactivatesDrinkAndInvalidatesCatalogAndMenuCaches()
    {
        var ct = TestContext.Current.CancellationToken;
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte", CoffeeDrinkCategory.Espresso, "", 10);
        var catalog = new Mock<ICoffeeDrinkRepository>();
        catalog.Setup(c => c.FindTrackedAsync("latte", ct)).ReturnsAsync(drink);
        var cache = new Mock<ICacheService>();
        var result = await UpdateCatalogDrinkHandler.Handle(new("latte", "Новое", "New", CoffeeDrinkCategory.Espresso, "", 20, false),
            catalog.Object, cache.Object, ct);
        result.Data!.IsActive.Should().BeFalse();
        result.Data!.NameRu.Should().Be("Новое");
        catalog.Verify(c => c.SaveAsync(ct), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKey.Shop.MenuDrinks()), Times.Once);
        cache.Verify(c => c.RemoveByPattern("shop:detail:*", ct), Times.Once);
    }
}
