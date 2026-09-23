using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Services;

public class EnrichShopFromImportServiceTests
{
    private readonly Mock<IQueryCoffeeShopRepository> _shopRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<EnrichShopFromImportService>> _logger = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private EnrichShopFromImportService CreateSut() =>
        new(_shopRepo.Object, _uow.Object, _cache.Object, _logger.Object);

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
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
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
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
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
}
