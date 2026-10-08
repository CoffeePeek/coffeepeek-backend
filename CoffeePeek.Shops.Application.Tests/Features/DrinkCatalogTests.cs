#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shops.Application.Features.Catalogs.Drinks;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;
using Moq;
using Wolverine;
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
    public async Task AdminUpdate_DeactivatesDrinkAndQueuesInvalidation()
    {
        var ct = TestContext.Current.CancellationToken;
        var drink = CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte", CoffeeDrinkCategory.Espresso, "", 10);
        var catalog = new Mock<ICoffeeDrinkRepository>();
        catalog.Setup(c => c.FindTrackedAsync("latte", ct)).ReturnsAsync(drink);
        var bus = new Mock<IMessageBus>();
        var result = await UpdateCatalogDrinkHandler.Handle(new("latte", "Новое", "New", CoffeeDrinkCategory.Espresso, "", 20, false),
            catalog.Object, bus.Object, ct);
        result.Data!.IsActive.Should().BeFalse();
        result.Data!.NameRu.Should().Be("Новое");
        bus.Verify(b => b.PublishAsync(It.IsAny<DrinkCatalogCacheInvalidationRequested>()), Times.Once);
    }

    [Fact]
    public async Task DuplicateCreate_DoesNotPublishInvalidation()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalog = new Mock<ICoffeeDrinkRepository>();
        catalog.Setup(c => c.FindTrackedAsync("latte", ct)).ReturnsAsync(
            CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte",
                CoffeeDrinkCategory.Espresso, "", 10));
        var bus = new Mock<IMessageBus>();

        await Assert.ThrowsAsync<CoffeePeek.Shared.Kernel.Exceptions.ConflictException>(() =>
            CreateCatalogDrinkHandler.Handle(new("latte", "Латте", "Latte",
                CoffeeDrinkCategory.Espresso, "", 10, true), catalog.Object, bus.Object, ct));

        catalog.Verify(c => c.Add(It.IsAny<CoffeeDrinkDefinition>()), Times.Never);
        bus.Verify(b => b.PublishAsync(It.IsAny<DrinkCatalogCacheInvalidationRequested>()), Times.Never);
    }
}
