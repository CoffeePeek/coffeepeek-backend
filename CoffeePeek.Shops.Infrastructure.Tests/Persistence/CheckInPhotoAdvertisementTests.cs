using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Modules;
using CoffeePeek.Shops.Persistance.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CheckInPhotoAdvertisementTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ShopsDbContext Context() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void StartupValidationRetainsServePrerequisiteForGlobalAdvertisement()
    {
        using (var invalid = Provider(true, false))
            Assert.Contains("requires serving", Assert.Throws<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<CheckInPhotoDeliveryOptions>>().Value).Message);
        using (var enabled = Provider(true, true))
            Assert.True(enabled.GetRequiredService<IOptions<CheckInPhotoDeliveryOptions>>().Value.AdvertiseVariants);
        using (var disabled = Provider(false, false))
            Assert.False(disabled.GetRequiredService<IOptions<CheckInPhotoDeliveryOptions>>().Value.AdvertiseVariants);
    }

    [Theory]
    [InlineData(true, "owner")]
    [InlineData(true, "other")]
    [InlineData(true, "anonymous")]
    [InlineData(false, "owner")]
    [InlineData(false, "other")]
    [InlineData(false, "anonymous")]
    public async Task GlobalAdvertisementFlagAppliesToEveryViewerAcrossPublicSurfaces(bool advertise, string reader)
    {
        await using var db = Context(); var owner = Guid.NewGuid();
        var rows = await PhotoAdvertisementTestData.SeedAsync(db, owner, Ct);
        Guid? viewer = reader == "owner" ? owner : reader == "other" ? Guid.NewGuid() : null;
        var options = Options.Create(Delivery(advertise));
        var queries = new CheckInQueries(db, PhotoAdvertisementTestData.Visibility(), options);
        var detail = (await queries.GetUnifiedByIdAsync(rows.Shop.Id, viewer, Ct))!;
        PhotoAdvertisementTestData.AssertPhotos(detail.Id, detail.Photos, detail.DrinkPhoto, advertise);
        var home = (await queries.GetHomeByIdAsync(rows.Home.Id, viewer, Ct))!;
        PhotoAdvertisementTestData.AssertPhotos(home.Id, home.Photos, home.DrinkPhoto, advertise);
        var publicPage = await queries.GetPublicAsync(viewer, null, null, owner, 10, null, Ct);
        Assert.Equal(2, publicPage.Items.Length);
        foreach (var item in publicPage.Items) PhotoAdvertisementTestData.AssertPhotos(item.Id, item.Photos, item.DrinkPhoto, advertise);
        var feed = await new CheckInFeedReader(db, PhotoAdvertisementTestData.Visibility(), options)
            .ReadAsync(new CheckInFeedScope(AuthorId: owner), 10, null, viewer, Ct);
        Assert.Equal(2, feed.Items.Length);
        foreach (var item in feed.Items) PhotoAdvertisementTestData.AssertPhotos(item.CheckIn.Id, item.CheckIn.Photos, item.CheckIn.DrinkPhoto, advertise);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerListsFollowGlobalFlagAndModerationNeverAdvertises(bool advertise)
    {
        await using var db = Context(); var owner = Guid.NewGuid();
        var rows = await PhotoAdvertisementTestData.SeedAsync(db, owner, Ct);
        var queries = new CheckInQueries(db, PhotoAdvertisementTestData.Visibility(), Options.Create(Delivery(advertise)));
        var mine = Assert.Single((await queries.GetUnifiedByUserAsync(owner, 1, 10, ct: Ct)).Items);
        PhotoAdvertisementTestData.AssertPhotos(mine.Id, mine.Photos, mine.DrinkPhoto, advertise);
        var shop = Assert.Single(await queries.GetByUserAndShopAsync(owner, rows.Shop.ShopId!.Value, ct: Ct));
        PhotoAdvertisementTestData.AssertPhotos(shop.Id, shop.Photos, shop.DrinkPhoto, advertise);
        var home = Assert.Single((await queries.GetHomeByUserAsync(owner, 1, 10, ct: Ct)).Items);
        PhotoAdvertisementTestData.AssertPhotos(home.Id, home.Photos, home.DrinkPhoto, advertise);
        var moderation = (await queries.GetModerationSnapshotAsync(rows.Shop.Id, rows.Shop.ContentRevision, Ct))!;
        PhotoAdvertisementTestData.AssertPhotos(moderation.Id, moderation.Photos, moderation.DrinkPhoto, false);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GlobalAdvertisementDoesNotExposePrivateHomePhotosToOtherOrAnonymousViewers()
    {
        await using var db = Context(); var owner = Guid.NewGuid();
        var rows = await PhotoAdvertisementTestData.SeedAsync(db, owner, Ct);
        var row = await db.CheckIns.SingleAsync(c => c.Id == rows.Home.Id, Ct);
        row.ChangeVisibility(CheckInVisibility.Private);
        await db.SaveChangesAsync(Ct); db.ChangeTracker.Clear();
        var queries = new CheckInQueries(db, PhotoAdvertisementTestData.Visibility(), Options.Create(Delivery(true)));
        var owned = (await queries.GetHomeByIdAsync(rows.Home.Id, owner, Ct))!;
        PhotoAdvertisementTestData.AssertPhotos(owned.Id, owned.Photos, owned.DrinkPhoto, true);
        foreach (var viewer in new Guid?[] { null, Guid.NewGuid() })
        {
            Assert.Null(await queries.GetHomeByIdAsync(rows.Home.Id, viewer, Ct));
            Assert.Null(await queries.GetUnifiedByIdAsync(rows.Home.Id, viewer, Ct));
            Assert.DoesNotContain((await queries.GetPublicAsync(viewer, null, null, owner, 10, null, Ct)).Items,
                item => item.Id == rows.Home.Id);
            Assert.DoesNotContain((await new CheckInFeedReader(db, PhotoAdvertisementTestData.Visibility(), Options.Create(Delivery(true)))
                .ReadAsync(new CheckInFeedScope(AuthorId: owner), 10, null, viewer, Ct)).Items,
                item => item.CheckIn.Id == rows.Home.Id);
        }
        Assert.Empty(db.ChangeTracker.Entries());
    }

    internal static CheckInPhotoDeliveryOptions Delivery(bool advertise) => new()
    {
        ServeVariants = true, AdvertiseVariants = advertise
    };

    private static ServiceProvider Provider(bool advertise, bool serve)
    {
        var values = new Dictionary<string, string?>
        {
            ["CheckInPhotoDeliveryOptions:AdvertiseVariants"] = advertise.ToString(),
            ["CheckInPhotoDeliveryOptions:ServeVariants"] = serve.ToString()
        };
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddCommunityModule(); return services.BuildServiceProvider();
    }
}
