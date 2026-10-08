using System;
using System.Threading.Tasks;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBeans;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllBrewMethods;
using CoffeePeek.Shops.Application.Features.Catalogs.GetAllEquipment;
using CoffeePeek.Shops.Application.Features.Catalogs.GetMenuDrinks;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Catalogs;

public class CatalogReadFreshnessTests
{
    [Fact]
    public async Task Equipment_ReadsChangedCatalogOnNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IQueryEquipmentRepository>();
        repository.SetupSequence(r => r.GetAll(ct))
            .ReturnsAsync([new Equipment("Hario", "V60", new EquipmentCategory())])
            .ReturnsAsync([new Equipment("Fellow", "Ode", new EquipmentCategory())]);
        var handler = new GetAllEquipmentHandler();
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var first = await handler.Handle(new GetAllEquipmentCommand(), repository.Object, mapper, ct);
        var second = await handler.Handle(new GetAllEquipmentCommand(), repository.Object, mapper, ct);

        first.Data!.Equipments.Should().ContainSingle().Which.Brand.Should().Be("Hario");
        second.Data!.Equipments.Should().ContainSingle().Which.Brand.Should().Be("Fellow");
        repository.Verify(r => r.GetAll(ct), Times.Exactly(2));
    }

    [Fact]
    public async Task Beans_ReadsChangedCatalogOnNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IQueryCoffeeBeanRepository>();
        repository.SetupSequence(r => r.GetAll(ct))
            .ReturnsAsync([new CoffeeBean("Ethiopia")])
            .ReturnsAsync([new CoffeeBean("Colombia")]);
        var handler = new GetAllBeansHandler();
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var first = await handler.Handle(new GetAllBeansCommand(), repository.Object, mapper, ct);
        var second = await handler.Handle(new GetAllBeansCommand(), repository.Object, mapper, ct);

        first.Data!.Beans.Should().ContainSingle().Which.Name.Should().Be("Ethiopia");
        second.Data!.Beans.Should().ContainSingle().Which.Name.Should().Be("Colombia");
        repository.Verify(r => r.GetAll(ct), Times.Exactly(2));
    }

    [Fact]
    public async Task BrewMethods_ReadsChangedCatalogOnNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IQueryBrewMethodRepository>();
        repository.SetupSequence(r => r.GetAll(ct))
            .ReturnsAsync([new BrewMethod("V60", BrewMethodCategory.Gravity)])
            .ReturnsAsync([new BrewMethod("Aeropress", BrewMethodCategory.Pressure)]);
        var handler = new GetAllBrewMethodsHandler();
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());

        var first = await handler.Handle(new GetAllBrewMethodsCommand(), repository.Object, mapper, ct);
        var second = await handler.Handle(new GetAllBrewMethodsCommand(), repository.Object, mapper, ct);

        first.Data!.BrewMethods.Should().ContainSingle().Which.Name.Should().Be("V60");
        second.Data!.BrewMethods.Should().ContainSingle().Which.Name.Should().Be("Aeropress");
        repository.Verify(r => r.GetAll(ct), Times.Exactly(2));
    }

    [Fact]
    public async Task MenuDrinks_ReadsChangedCatalogOnNextRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IQueryCoffeeDrinkRepository>();
        repository.SetupSequence(r => r.GetActiveAsync(ct))
            .ReturnsAsync([CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "latte", "Латте", "Latte", CoffeeDrinkCategory.Espresso, "", 0)])
            .ReturnsAsync([CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "filter", "Фильтр", "Filter", CoffeeDrinkCategory.Filter, "", 0)]);

        var first = await GetMenuDrinksHandler.Handle(new GetMenuDrinksQuery(), repository.Object, ct);
        var second = await GetMenuDrinksHandler.Handle(new GetMenuDrinksQuery(), repository.Object, ct);

        first.Data!.Drinks.Should().ContainSingle().Which.Slug.Should().Be("latte");
        second.Data!.Drinks.Should().ContainSingle().Which.Slug.Should().Be("filter");
        repository.Verify(r => r.GetActiveAsync(ct), Times.Exactly(2));
    }
}
