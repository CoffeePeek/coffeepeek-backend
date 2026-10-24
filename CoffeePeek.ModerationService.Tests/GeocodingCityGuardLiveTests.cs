using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Testing;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeeShop.Moderation.Persistence.Repositories;

namespace CoffeePeek.ModerationService.Tests;

[Trait("Category", LivePostgresServer.Category)]
public class GeocodingCityGuardLiveTests : IClassFixture<GeocodingLiveDatabase>
{
    private readonly GeocodingLiveDatabase _fixture;

    public GeocodingCityGuardLiveTests(GeocodingLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        _fixture = fixture;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameStreetInChangedCity_RejectsOldCoordinatesAndAppliesOnlyCurrentCity(bool roaster)
    {
        var ct = TestContext.Current.CancellationToken;
        var originalCity = Guid.NewGuid();
        var correctedCity = Guid.NewGuid();
        var shop = ModerationShop.Create("Cafe", null, originalCity, null);
        shop.SetLocation(new ModerationLocation("Советская, 10"));
        var roast = ModerationRoaster.Create("Roaster", null, null);
        roast.SetLocation(originalCity, new ModerationLocation("Советская, 10"));
        var id = roaster ? roast.Id : shop.Id;
        await using (var seed = _fixture.Context())
        {
            if (roaster) seed.ModerationRoasters.Add(roast);
            else seed.ModerationShops.Add(shop);
            await seed.SaveChangesAsync(ct);
        }

        await using var db = _fixture.Context();
        var shops = new ModerationShopRepository(db);
        var roasters = new ModerationRoasterRepository(db);
        Assert.Equal(originalCity, await CityAsync());
        await using (var edit = _fixture.Context())
        {
            if (roaster)
                (await new ModerationRoasterRepository(edit).GetByIdAsync(id, ct))!
                    .SetLocation(correctedCity, new ModerationLocation("Советская, 10"));
            else
                (await new ModerationShopRepository(edit).GetByIdAsync(id, ct))!
                    .UpdateInfo("Cafe", null, null, correctedCity, null);
            await edit.SaveChangesAsync(ct);
        }

        Assert.False(await ApplyAsync(originalCity));
        Assert.Equal(correctedCity, await CityAsync());
        Assert.True(await ApplyAsync(correctedCity));
        Assert.False(await ApplyAsync(correctedCity));
        Assert.Null(await CityAsync());

        await using var verify = _fixture.Context();
        var location = roaster
            ? (await new ModerationRoasterRepository(verify).GetByIdAsync(id, ct))!.Location
            : (await new ModerationShopRepository(verify).GetByIdAsync(id, ct))!.Location;
        Assert.True(location!.IsAddressValidated);
        Assert.Equal("Советская, 10", location.Address);
        Assert.Equal(52.1m, location.Latitude);
        Assert.Equal(23.7m, location.Longitude);

        Task<Guid?> CityAsync() => roaster
            ? roasters.GetAwaitingGeocodingCityIdAsync(id, "Советская, 10", ct)
            : shops.GetAwaitingGeocodingCityIdAsync(id, "Советская, 10", ct);
        Task<bool> ApplyAsync(Guid cityId) => roaster
            ? roasters.TryApplyGeocodingAsync(id, "Советская, 10", cityId, 52.1m, 23.7m, ct)
            : shops.TryApplyGeocodingAsync(id, "Советская, 10", cityId, 52.1m, 23.7m, ct);
    }
}
