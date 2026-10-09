using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Services;

public class EnrichShopFromImportServiceTests
{
    private readonly Mock<IQueryCoffeeShopRepository> _shopRepo = new();
    private readonly Mock<Wolverine.IMessageBus> _bus = new();
    private readonly Mock<ILogger<EnrichShopFromImportService>> _logger = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private EnrichShopFromImportService CreateSut() =>
        new(_shopRepo.Object, _bus.Object, _logger.Object);

    [Fact]
    public async Task Enrich_FillsMissingFieldsOnMatchingShop()
    {
        var shop = new CoffeeShop(Guid.NewGuid(), "Coffe Joy", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Минск", 53.9152m, 27.5847m);
        shop.SetContact(null, null, null, null);
        _shopRepo.Setup(r => r.ListForEnrichmentAsync(It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), _ct)).ReturnsAsync([shop]);

        var count = await CreateSut().EnrichShopsFromImportAsync(
        [
            new ImportShopEnrichmentItem(
                null,
                "Coffe Joy",
                "Немига 5, Минск",
                53.91525m,
                27.58475m,
                "+375291112233",
                "https://coffejoy.by",
                "https://instagram.com/coffejoy")
        ], _ct);

        count.Should().Be(1);
        shop.Contact.PhoneNumber.Should().Be("+375291112233");
        shop.Contact.SiteLink.Should().Be("https://coffejoy.by");
        shop.Location.Address.Should().Be("Немига 5, Минск");
        _bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            e => e.ShopId == shop.Id && e.AdditionalShopIds.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task Enrich_WhenNoMatchingShop_DoesNotSave()
    {
        var shop = new CoffeeShop(Guid.NewGuid(), "Other Cafe", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Минск", 53.90m, 27.50m);
        _shopRepo.Setup(r => r.ListForEnrichmentAsync(It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), _ct)).ReturnsAsync([shop]);

        var count = await CreateSut().EnrichShopsFromImportAsync(
        [
            new ImportShopEnrichmentItem(null, "Coffe Joy", "Немига 5", 53.9152m, 27.5847m, null, null, null)
        ], _ct);

        count.Should().Be(0);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ShopProfileCacheInvalidationRequested>()), Times.Never);
    }

    [Fact]
    public async Task Enrich_UsesShopIdWhenProvided()
    {
        var shop = new CoffeeShop(Guid.NewGuid(), "Coffe Joy", null, PriceRange.Moderate, Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Минск", 53.9152m, 27.5847m);
        shop.SetContact(null, null, null, null);
        _shopRepo.Setup(r => r.ListForEnrichmentAsync(It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), _ct)).ReturnsAsync([shop]);

        var count = await CreateSut().EnrichShopsFromImportAsync(
        [
            new ImportShopEnrichmentItem(
                shop.Id,
                "Totally Different Name",
                "Немига 5, Минск",
                53.0m,
                27.0m,
                "+375291112233",
                null,
                null)
        ], _ct);

        count.Should().Be(1);
        shop.Contact.PhoneNumber.Should().Be("+375291112233");
    }

    [Fact]
    public async Task Enrich_TwoShops_PublishesOneBatchInsteadOfTwoGlobalInvalidations()
    {
        var first = new CoffeeShop(Guid.NewGuid(), "First", null, PriceRange.Moderate, Guid.NewGuid());
        var second = new CoffeeShop(Guid.NewGuid(), "Second", null, PriceRange.Moderate, Guid.NewGuid());
        first.SetLocation(Guid.NewGuid(), "Old 1", 53m, 27m);
        second.SetLocation(Guid.NewGuid(), "Old 2", 54m, 28m);
        _shopRepo.Setup(r => r.ListForEnrichmentAsync(
            It.IsAny<System.Collections.Generic.IReadOnlyCollection<Guid>>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), _ct))
            .ReturnsAsync([first, second]);

        var count = await CreateSut().EnrichShopsFromImportAsync([
            new ImportShopEnrichmentItem(first.Id, "First", "New 1", 53m, 27m, "111", null, null),
            new ImportShopEnrichmentItem(second.Id, "Second", "New 2", 54m, 28m, "222", null, null)
        ], _ct);

        count.Should().Be(2);
        _bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(
            e => e.AdditionalShopIds.Count == 1 &&
                ((e.ShopId == first.Id && e.AdditionalShopIds[0] == second.Id) ||
                 (e.ShopId == second.Id && e.AdditionalShopIds[0] == first.Id)))), Times.Once);
    }
}
