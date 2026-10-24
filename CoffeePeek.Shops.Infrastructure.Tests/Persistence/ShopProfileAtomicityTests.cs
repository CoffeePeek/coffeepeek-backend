using CoffeePeek.Shared.Kernel.Geocoding;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class ShopProfileAtomicityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly IOptions<MediaPublicUrlOptions> Media = Options.Create(
        new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test", ShopBucketName = "shops" });

    public static IEnumerable<object[]> InvalidPatches()
    {
        foreach (var owner in new[] { false, true })
        foreach (var failure in new[] { "city", "location", "contact", "schedule", "equipment", "beans", "roasters", "brew" })
            yield return [owner, failure];
    }

    [Theory]
    [MemberData(nameof(InvalidPatches))]
    public async Task InvalidPatch_RemainsUnchangedEvenWhenPipelineSaves(bool owner, string failure)
    {
        await using var db = CreateDb();
        var shop = await SeedAsync(db);
        var before = Snapshot(shop);
        var city = new City("Минск");
        var cityId = city.Id;
        var cities = new Mock<IQueryCityRepository>();
        cities.Setup(x => x.GetById(cityId, Ct)).ReturnsAsync(failure == "city" ? null : city);
        var equipment = new Mock<IQueryEquipmentRepository>();
        equipment.Setup(x => x.GetByIds(It.IsAny<List<Guid>>(), Ct)).ReturnsAsync([]);
        var beans = new Mock<IQueryCoffeeBeanRepository>();
        beans.Setup(x => x.GetByIds(It.IsAny<List<Guid>>(), Ct)).ReturnsAsync([]);
        var roasters = new Mock<IQueryRoasterRepository>();
        roasters.Setup(x => x.GetByIds(It.IsAny<List<Guid>>(), Ct)).ReturnsAsync([]);
        var brew = new Mock<IQueryBrewMethodRepository>();
        brew.Setup(x => x.GetByIds(It.IsAny<List<Guid>>(), Ct)).ReturnsAsync([]);
        var cache = new Mock<ICacheService>(MockBehavior.Strict);
        var location = new ShopLocationPatch(cityId, "New address", failure == "location" ? 91m : 54m, 28m);
        var contacts = new ShopContactsPatch(failure == "contact" ? new string('1', 1000) : "123", null, null, null);
        var day = new ScheduleDto(DayOfWeek.Tuesday, true, []);
        ScheduleDto[] schedules = failure == "schedule" ? [day, day] : [day];
        Guid[] Ids(string name) => failure == name ? [Guid.NewGuid()] : [];
        var catalogs = new ShopCatalogsPatch(Ids("equipment"), Ids("beans"), Ids("roasters"), Ids("brew"));
        var repository = new CoffeeShopRepository(db);
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var geocoder = new Mock<IAddressGeocoder>();
        geocoder.Setup(g => g.GeocodeAsync("Минск, New address", Ct)).ReturnsAsync(new GeocodingResult(54m, 28m));

        Func<Task> action = owner
            ? () => UpdateOwnerCoffeeShopHandler.Handle(
                new(shop.Id, shop.OwnerUserId!.Value, "Changed", "Changed description", contacts.PhoneNumber,
                    contacts.Email, contacts.SiteLink, contacts.InstagramLink, location, schedules, catalogs),
                repository, cities.Object, equipment.Object, beans.Object, roasters.Object, brew.Object,
                bus.Object, Media, geocoder.Object, Ct)
            : () => UpdateAdminCoffeeShopHandler.Handle(
                new(shop.Id, "Changed", "Changed description", CoffeePeek.Contract.Enums.PriceRange.Moderate,
                    CoffeeShopStatus.TemporarilyClosed, location, contacts, schedules, catalogs),
                repository, cities.Object, equipment.Object, beans.Object, roasters.Object, brew.Object,
                bus.Object, Media, geocoder.Object, Ct);

        await Assert.ThrowsAsync<ValidationException>(action);
        Assert.False(db.ChangeTracker.HasChanges());
        // Model the unconditional save in Wolverine's generated EF transaction adapter.
        // This deliberately tests tracked state + persistence, not a mock UoW call count.
        Assert.Equal(0, await db.SaveChangesAsync(Ct));
        db.ChangeTracker.Clear();
        var persisted = await repository.GetByIdWithCatalogsAsync(shop.Id, Ct);
        Assert.Equal(before, Snapshot(persisted!));
        cache.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ValidPatch_PersistsAndPreservesNullVersusEmptyCollections(bool owner, bool clearCollections)
    {
        await using var db = CreateDb();
        var shop = await SeedAsync(db);
        var city = new City("Минск");
        var cityId = city.Id;
        var cities = new Mock<IQueryCityRepository>();
        cities.Setup(x => x.GetById(cityId, Ct)).ReturnsAsync(city);
        var cache = new Mock<ICacheService>();
        var repository = new CoffeeShopRepository(db);
        var location = new ShopLocationPatch(cityId, "New address", 54m, 28m);
        ScheduleDto[]? schedules = clearCollections ? [] : null;
        var catalogs = clearCollections ? new ShopCatalogsPatch([], [], [], []) : null;
        var bus = new Mock<IMessageBus>();
        var geocoder = new Mock<IAddressGeocoder>();
        geocoder.Setup(g => g.GeocodeAsync("Минск, New address", Ct)).ReturnsAsync(new GeocodingResult(54m, 28m));
        var response = owner
            ? await UpdateOwnerCoffeeShopHandler.Handle(
                new(shop.Id, shop.OwnerUserId!.Value, "Changed", "Description", "123", null, null, null,
                    location, schedules, catalogs), repository, cities.Object,
                Mock.Of<IQueryEquipmentRepository>(), Mock.Of<IQueryCoffeeBeanRepository>(),
                Mock.Of<IQueryRoasterRepository>(), Mock.Of<IQueryBrewMethodRepository>(), bus.Object, Media, geocoder.Object, Ct)
            : await UpdateAdminCoffeeShopHandler.Handle(
                new(shop.Id, "Changed", "Description", CoffeePeek.Contract.Enums.PriceRange.Moderate,
                    CoffeeShopStatus.TemporarilyClosed, location, new("123", null, null, null), schedules, catalogs),
                repository, cities.Object, Mock.Of<IQueryEquipmentRepository>(), Mock.Of<IQueryCoffeeBeanRepository>(),
                Mock.Of<IQueryRoasterRepository>(), Mock.Of<IQueryBrewMethodRepository>(), bus.Object, Media, geocoder.Object, Ct);

        Assert.True(response.IsSuccess);
        bus.Verify(b => b.PublishAsync(It.Is<ShopProfileCacheInvalidationRequested>(e => e.ShopId == shop.Id)), Times.Once);
        cache.VerifyNoOtherCalls();
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var persisted = (await repository.GetByIdWithCatalogsAsync(shop.Id, Ct))!;
        Assert.Equal("Changed", persisted.Name);
        Assert.Equal("Description", persisted.Description);
        Assert.Equal(cityId, persisted.Location.CityId);
        Assert.Equal("123", persisted.Contact.PhoneNumber);
        Assert.Equal(owner ? PriceRange.Cheap : PriceRange.Moderate, persisted.PriceRange);
        Assert.Equal(owner ? CoffeeShopStatus.Active : CoffeeShopStatus.TemporarilyClosed, persisted.Status);
        Assert.Equal(clearCollections ? 0 : 1, persisted.Schedules.Count);
        Assert.Equal(clearCollections ? 0 : 1, persisted.Equipments.Count);
    }

    private static ShopsDbContext CreateDb() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<CoffeeShop> SeedAsync(ShopsDbContext db)
    {
        var shop = new CoffeeShop(null, "Original", "Original description", PriceRange.Cheap, Guid.NewGuid());
        shop.AssignOwner(Guid.NewGuid());
        shop.SetLocation(Guid.NewGuid(), "Original address", 53m, 27m);
        shop.SetContact(null, "original@example.test", null, "456");
        shop.ReplaceSchedules([ShopSchedule.Create(DayOfWeek.Monday, false,
            [ShopScheduleInterval.Create(TimeSpan.FromHours(8), TimeSpan.FromHours(18))])]);
        var category = new EquipmentCategory();
        db.Entry(category).Property(x => x.Name).CurrentValue = "Machine";
        shop.AddEquipment(new Equipment("Original brand", "Original model", category));
        db.Shops.Add(shop);
        await db.SaveChangesAsync(Ct);
        return shop;
    }

    private static string Snapshot(CoffeeShop shop) => JsonSerializer.Serialize(AdminPublishedShopMapper.Map(shop, Media.Value));
}
